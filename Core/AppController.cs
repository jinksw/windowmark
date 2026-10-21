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
            Exit);

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

        var target = WindowService.Describe(hwnd);
        if (target == null)
        {
            _tray.ShowInfo("窗口已关闭或不可标记");
            return;
        }

        _editorOpen = true;
        try
        {
            var existing = _overlays.FindRuleFor(target);
            string identity = target.ExePath + "|" + target.ClassName + "|" + target.NativeTitle;
            Log.Info($"打开标签编辑器: hwnd={target.Hwnd} exe={target.ExePath} class={target.ClassName} title='{target.NativeTitle}' existing='{existing?.Label}'");
            var editor = new LabelEditorWindow(target, existing, Palette.DefaultFor(identity));

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
                if (existing != null) _overlays.RemoveRule(existing);
                return;
            }

            var rule = new LabelRule
            {
                ExePath = target.ExePath,
                ClassName = target.ClassName,
                NativeTitle = target.NativeTitle,
                Label = editor.ResultLabel,
                Color = editor.ResultColor
            };

            if (existing != null && !existing.SameIdentity(rule)) _overlays.RemoveRule(existing);
            _overlays.ApplyRule(target, rule, suppressSiblings: true);
        }
        catch (Exception ex)
        {
            Log.Error("打标签失败", ex);
            _tray.ShowInfo("操作失败，详情见标签数据文件夹中的 log.txt");
        }
        finally
        {
            _editorOpen = false;
        }
    }

    private static void Exit()
    {
        Application.Current.Shutdown();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _hotkey.Dispose();
        _tray.Dispose();
        _overlays.Dispose();
    }
}
