using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Threading;
using WindowMark.Native;
using WindowMark.UI;

namespace WindowMark.Core;

internal sealed class OverlayManager : IDisposable
{
    private sealed class PreviewSession
    {
        internal PreviewSession(
            IntPtr hwnd,
            OverlayWindow overlay,
            LabelRule? originalRule,
            bool wasSuppressed)
        {
            Hwnd = hwnd;
            Overlay = overlay;
            OriginalRule = originalRule;
            WasSuppressed = wasSuppressed;
        }

        internal IntPtr Hwnd { get; }
        internal OverlayWindow Overlay { get; }
        internal LabelRule? OriginalRule { get; }
        internal bool WasSuppressed { get; }
    }

    private readonly LabelStore _store;
    private readonly Dictionary<IntPtr, OverlayWindow> _overlays = new();
    private readonly HashSet<IntPtr> _suppressed = new();
    private readonly DispatcherTimer _placementTimer;
    private readonly DispatcherTimer _maintenanceTimer;
    private PreviewSession? _preview;
    private OverlayWindow? _selected;
    private bool _layoutMode;
    private bool _placementErrorLogged;
    private bool _disposed;

    internal event EventHandler? LayoutModeChanged;
    internal Action<OverlayWindow?>? LayoutSelectionChanged;
    internal event EventHandler? LayoutUpdated;

    internal OverlayManager(LabelStore store)
    {
        _store = store;

        _placementTimer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(33)
        };
        _placementTimer.Tick += (_, _) => SyncAll();

        _maintenanceTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _maintenanceTimer.Tick += (_, _) => Maintain();
    }

    internal bool IsLayoutMode => _layoutMode;
    internal OverlayWindow? SelectedOverlay => _selected;

    internal void Start()
    {
        ApplySavedRules();
        _placementTimer.Start();
        _maintenanceTimer.Start();
    }

    internal LabelRule? FindRuleFor(TargetWindow target)
    {
        var activeRule = _overlays.TryGetValue(target.Hwnd, out var overlay) ? overlay.Rule : null;
        return activeRule ?? _store.Find(target);
    }

    internal void ApplyRule(TargetWindow target, LabelRule rule, bool suppressSiblings)
    {
        _store.Upsert(rule);

        foreach (var overlay in _overlays.Values.ToList())
        {
            if (overlay.Rule.SameIdentity(rule)) overlay.ApplyRule(rule);
        }

        ShowOverlay(target, rule);

        if (suppressSiblings) SuppressSiblings(rule, target.Hwnd);
    }

    internal void RemoveRule(LabelRule rule)
    {
        _store.Remove(rule);

        foreach (var (hwnd, overlay) in _overlays.ToList())
        {
            if (overlay.Rule.SameIdentity(rule)) CloseOverlay(hwnd);
        }
    }

    internal void PreviewRule(TargetWindow target, LabelRule rule)
    {
        if (_disposed || !NativeMethods.IsWindow(target.Hwnd)) return;

        if (string.IsNullOrWhiteSpace(rule.Label))
        {
            CancelPreview();
            return;
        }

        if (_preview == null
            || _preview.Hwnd != target.Hwnd
            || !_overlays.TryGetValue(target.Hwnd, out var current)
            || current != _preview.Overlay)
        {
            CancelPreview();

            if (_overlays.TryGetValue(target.Hwnd, out var existing))
            {
                _preview = new PreviewSession(target.Hwnd, existing, existing.Rule.Clone(), false);
            }
            else
            {
                bool wasSuppressed = _suppressed.Contains(target.Hwnd);
                ShowOverlay(target, rule);
                if (!_overlays.TryGetValue(target.Hwnd, out var created)) return;
                _preview = new PreviewSession(target.Hwnd, created, null, wasSuppressed);
            }
        }

        _preview.Overlay.ApplyRule(rule);
    }

    internal void CommitPreview()
    {
        _preview = null;
    }

    internal void CancelPreview()
    {
        var preview = _preview;
        _preview = null;
        if (preview == null) return;

        if (!_overlays.TryGetValue(preview.Hwnd, out var overlay) || overlay != preview.Overlay)
            return;

        if (preview.OriginalRule != null)
        {
            overlay.ApplyRule(preview.OriginalRule);
            return;
        }

        CloseOverlay(preview.Hwnd);
        if (preview.WasSuppressed && NativeMethods.IsWindow(preview.Hwnd))
            _suppressed.Add(preview.Hwnd);
    }

    internal bool EnterLayoutMode()
    {
        if (_disposed) return false;
        if (_layoutMode) return true;

        var first = _overlays.Values.FirstOrDefault(overlay => overlay.IsShown);
        if (first == null) return false;

        _layoutMode = true;
        foreach (var overlay in _overlays.Values) overlay.SetLayoutMode(true);

        LayoutModeChanged?.Invoke(this, EventArgs.Empty);
        SelectOverlay(first);
        return true;
    }

    internal void ExitLayoutMode()
    {
        if (!_layoutMode) return;

        SelectOverlay(null);
        foreach (var overlay in _overlays.Values) overlay.SetLayoutMode(false);
        _layoutMode = false;
        LayoutModeChanged?.Invoke(this, EventArgs.Empty);
    }

    internal void ApplyPositionPreset(LabelAnchor anchor)
    {
        var preset = LabelLayout.PositionPreset(anchor);
        ApplyLayoutToSelected(rule =>
        {
            rule.Anchor = preset.Anchor;
            rule.OffsetX = preset.OffsetX;
            rule.OffsetY = preset.OffsetY;
        });
    }

    internal void ApplySizePreset(double scale)
    {
        ApplyLayoutToSelected(rule =>
            rule.Scale = Math.Clamp(scale, LabelLayout.MinScale, LabelLayout.MaxScale));
    }

    internal void ResetSelectedLayout()
    {
        ApplyLayoutToSelected(rule => rule.ResetLayout());
    }

    private void SelectOverlay(OverlayWindow? overlay)
    {
        if (_selected == overlay) return;

        _selected?.SetSelected(false);
        _selected = overlay;
        _selected?.SetSelected(true);
        LayoutSelectionChanged?.Invoke(_selected);
    }

    private void ApplyLayoutToSelected(Action<LabelRule> mutate)
    {
        if (_selected == null) return;

        var updated = _selected.Rule.Clone();
        mutate(updated);
        updated.SanitizeLayout();
        SaveAndApplyLayout(updated);
    }

    private void OnOverlayLayoutChanged(OverlayWindow source)
    {
        var updated = source.Rule.Clone();
        updated.SanitizeLayout();
        SaveAndApplyLayout(updated);
    }

    private void SaveAndApplyLayout(LabelRule updated)
    {
        _store.Upsert(updated);

        foreach (var overlay in _overlays.Values.ToList())
        {
            if (overlay.Rule.SameIdentity(updated)) overlay.ApplyRule(updated);
        }

        LayoutUpdated?.Invoke(this, EventArgs.Empty);
    }

    private void ApplySavedRules()
    {
        if (_store.Rules.Count == 0) return;

        foreach (var hwnd in WindowService.EnumerateTopLevelWindows())
        {
            var target = WindowService.Describe(hwnd, requireTitle: true);
            if (target == null) continue;

            var rule = _store.Find(target);
            if (rule != null) ShowOverlay(target, rule);
        }
    }

    private void Maintain()
    {
        if (_disposed) return;

        try
        {
            foreach (var hwnd in _overlays.Keys.ToList())
            {
                if (!NativeMethods.IsWindow(hwnd)) CloseOverlay(hwnd);
            }

            _suppressed.RemoveWhere(hwnd => !NativeMethods.IsWindow(hwnd));

            if (_store.Rules.Count == 0) return;

            foreach (var hwnd in WindowService.EnumerateTopLevelWindows())
            {
                if (_overlays.ContainsKey(hwnd) || _suppressed.Contains(hwnd)) continue;

                var target = WindowService.Describe(hwnd, requireTitle: true);
                if (target == null) continue;

                var rule = _store.Find(target);
                if (rule != null) ShowOverlay(target, rule);
            }
        }
        catch (Exception ex)
        {
            Log.Error("窗口扫描失败", ex);
        }
    }

    private void SyncAll()
    {
        if (_disposed) return;

        foreach (var overlay in _overlays.Values)
        {
            if (overlay.IsAdjusting) continue;

            try
            {
                overlay.SyncToTarget();
            }
            catch (Exception ex)
            {
                if (_placementErrorLogged) continue;
                _placementErrorLogged = true;
                Log.Error("更新标签位置失败", ex);
            }
        }
    }

    private void ShowOverlay(TargetWindow target, LabelRule rule)
    {
        if (_overlays.TryGetValue(target.Hwnd, out var existing))
        {
            existing.ApplyRule(rule);
            return;
        }

        var overlay = new OverlayWindow(target, rule);
        overlay.Selected += (_, _) =>
        {
            if (_layoutMode) SelectOverlay(overlay);
        };
        overlay.LayoutChanged += (_, _) => OnOverlayLayoutChanged(overlay);

        _overlays[target.Hwnd] = overlay;
        _suppressed.Remove(target.Hwnd);

        if (_layoutMode) overlay.SetLayoutMode(true);

        overlay.Show();
        overlay.SyncToTarget();
    }

    private void CloseOverlay(IntPtr hwnd)
    {
        if (!_overlays.Remove(hwnd, out var overlay)) return;

        if (_preview != null && _preview.Overlay == overlay) _preview = null;

        bool wasSelected = _selected == overlay;
        if (wasSelected) SelectOverlay(null);

        try { overlay.Close(); } catch { }

        if (!wasSelected || !_layoutMode) return;

        var next = _overlays.Values.FirstOrDefault(candidate => candidate.IsShown);
        if (next != null) SelectOverlay(next);
        else ExitLayoutMode();
    }

    private void SuppressSiblings(LabelRule rule, IntPtr exceptHwnd)
    {
        foreach (var hwnd in WindowService.EnumerateTopLevelWindows())
        {
            if (hwnd == exceptHwnd || _overlays.ContainsKey(hwnd)) continue;

            var target = WindowService.Describe(hwnd, requireTitle: true);
            if (target != null && rule.Matches(target)) _suppressed.Add(hwnd);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _placementTimer.Stop();
        _maintenanceTimer.Stop();

        foreach (var overlay in _overlays.Values.ToList())
        {
            try { overlay.Close(); } catch { }
        }
        _overlays.Clear();
    }
}
