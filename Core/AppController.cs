using System;
using System.Windows;
using WindowMark.Native;
using WindowMark.UI;

namespace WindowMark.Core;

internal sealed class AppController : IDisposable
{
    private readonly OverlayManager _overlays;
    private readonly HotkeyWindow _hotkey;
    private readonly TrayController _tray;
    private LayoutToolbarWindow? _layoutToolbar;
    private bool _editorOpen;
    private bool _disposed;

    internal AppController()
    {
        var store = LabelStore.Load();
        _overlays = new OverlayManager(store);

        _hotkey = new HotkeyWindow();
        _hotkey.Pressed += (_, _) => TagWindowUnderCursor();

        _tray = new TrayController(
            () => WindowService.ListCandidates(),
            hwnd => Application.Current.Dispatcher.BeginInvoke(new Action(() => BeginTag(hwnd))),
            () => Application.Current.Dispatcher.BeginInvoke(new Action(ToggleLayoutMode)),
            Exit);

        _overlays.LayoutModeChanged += (_, _) => OnLayoutModeChanged();
        _overlays.LayoutSelectionChanged += OnLayoutSelectionChanged;
        _overlays.LayoutUpdated += (_, _) => OnLayoutUpdated();

        _overlays.Start();
        _tray.ShowStartup(_hotkey.Registered);
    }

    private void TagWindowUnderCursor()
    {
        if (_editorOpen || _disposed) return;

        var hwnd = WindowService.GetWindowUnderCursor();
        if (hwnd == IntPtr.Zero)
        {
            _tray.ShowInfo("鼠标下没有可标记的窗口");
            return;
        }

        BeginTag(hwnd);
    }

    private void BeginTag(IntPtr hwnd)
    {
        if (_editorOpen || _disposed) return;

        if (_overlays.IsLayoutMode)
        {
            _tray.ShowInfo("请先完成标签位置调整");
            return;
        }

        var target = WindowService.Describe(hwnd);
        if (target == null)
        {
            _tray.ShowInfo("窗口已关闭或不可标记");
            return;
        }

        _editorOpen = true;
        bool previewCommitted = false;
        try
        {
            var existing = _overlays.FindRuleFor(target);
            string identity = target.ExePath + "|" + target.ClassName + "|" + target.NativeTitle;
            Log.Info($"打开标签编辑器: hwnd={target.Hwnd} exe={target.ExePath} class={target.ClassName} title='{target.NativeTitle}' existing='{existing?.Label}'");
            var editor = new LabelEditorWindow(
                target,
                existing,
                Palette.DefaultFor(identity),
                preview => PreviewLabel(target, preview));

            if (editor.ShowDialog() != true || editor.Action == LabelEditorAction.Cancel)
            {
                Log.Info($"取消标签编辑: action={editor.Action}");
                return;
            }

            Log.Info($"标签编辑结果: action={editor.Action} label='{editor.ResultLabel}' color={editor.ResultColor}");

            if (!NativeMethods.IsWindow(target.Hwnd))
            {
                _tray.ShowInfo("窗口已关闭，标签未保存");
                return;
            }

            if (editor.Action == LabelEditorAction.Delete || editor.ResultLabel.Length == 0)
            {
                _overlays.CancelPreview();
                if (existing != null) _overlays.RemoveRule(existing);
                return;
            }

            var rule = new LabelRule
            {
                ExePath = target.ExePath,
                ClassName = target.ClassName,
                NativeTitle = target.NativeTitle,
                Label = editor.ResultLabel,
                Color = editor.ResultColor,
                Anchor = editor.ResultAnchor,
                OffsetX = editor.ResultOffsetX,
                OffsetY = editor.ResultOffsetY,
                Scale = editor.ResultScale
            };

            if (existing != null && !existing.SameIdentity(rule)) _overlays.RemoveRule(existing);
            _overlays.ApplyRule(target, rule, suppressSiblings: true);
            _overlays.CommitPreview();
            previewCommitted = true;
        }
        catch (Exception ex)
        {
            Log.Error("打标签失败", ex);
            _tray.ShowInfo("操作失败，详情见标签数据文件夹中的 log.txt");
        }
        finally
        {
            if (!previewCommitted) _overlays.CancelPreview();
            _editorOpen = false;
        }
    }

    private void PreviewLabel(TargetWindow target, LabelPreview preview)
    {
        var rule = new LabelRule
        {
            ExePath = target.ExePath,
            ClassName = target.ClassName,
            NativeTitle = target.NativeTitle,
            Label = preview.Label,
            Color = preview.Color,
            Anchor = preview.Anchor,
            OffsetX = preview.OffsetX,
            OffsetY = preview.OffsetY,
            Scale = preview.Scale
        };

        _overlays.PreviewRule(target, rule);
    }

    private void ToggleLayoutMode()
    {
        if (_disposed) return;

        if (_overlays.IsLayoutMode)
        {
            _overlays.ExitLayoutMode();
            return;
        }

        if (_editorOpen)
        {
            _tray.ShowInfo("请先关闭标签编辑器");
            return;
        }

        if (!_overlays.EnterLayoutMode())
        {
            _tray.ShowInfo("当前没有可调整的标签");
            return;
        }

        _tray.ShowInfo("拖动标签移动，拖右下角缩放；点“完成”退出。");
    }

    private void OnLayoutModeChanged()
    {
        bool active = _overlays.IsLayoutMode;
        _tray.SetLayoutModeActive(active);

        if (!active)
        {
            CloseLayoutToolbar();
            return;
        }

        EnsureLayoutToolbar();
        if (_overlays.SelectedOverlay != null)
            _layoutToolbar?.ShowFor(_overlays.SelectedOverlay);
    }

    private void OnLayoutSelectionChanged(OverlayWindow? overlay)
    {
        if (!_overlays.IsLayoutMode || _layoutToolbar == null) return;
        _layoutToolbar.ShowFor(overlay);
    }

    private void OnLayoutUpdated()
    {
        if (!_overlays.IsLayoutMode || _layoutToolbar == null) return;
        _layoutToolbar.ShowFor(_overlays.SelectedOverlay);
    }

    private void EnsureLayoutToolbar()
    {
        if (_layoutToolbar != null) return;

        var toolbar = new LayoutToolbarWindow();
        toolbar.Done += () => _overlays.ExitLayoutMode();
        toolbar.PositionPresetRequested += anchor => _overlays.ApplyPositionPreset(anchor);
        toolbar.SizePresetRequested += scale => _overlays.ApplySizePreset(scale);
        toolbar.ResetRequested += () => _overlays.ResetSelectedLayout();
        _layoutToolbar = toolbar;
    }

    private void CloseLayoutToolbar()
    {
        var toolbar = _layoutToolbar;
        _layoutToolbar = null;
        if (toolbar == null) return;

        try { toolbar.Close(); } catch { }
    }

    private static void Exit()
    {
        Application.Current.Shutdown();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        CloseLayoutToolbar();
        _hotkey.Dispose();
        _tray.Dispose();
        _overlays.Dispose();
    }
}
