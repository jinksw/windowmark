using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Threading;
using WindowMark.Native;
using WindowMark.UI;

namespace WindowMark.Core;

internal sealed class OverlayManager : IDisposable
{
    private readonly LabelStore _store;
    private readonly Dictionary<IntPtr, OverlayWindow> _overlays = new();
    private readonly HashSet<IntPtr> _suppressed = new();
    private readonly DispatcherTimer _placementTimer;
    private readonly DispatcherTimer _maintenanceTimer;
    private bool _placementErrorLogged;
    private bool _disposed;

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
        _overlays[target.Hwnd] = overlay;
        _suppressed.Remove(target.Hwnd);

        overlay.Show();
        overlay.SyncToTarget();
    }

    private void CloseOverlay(IntPtr hwnd)
    {
        if (!_overlays.Remove(hwnd, out var overlay)) return;
        try { overlay.Close(); } catch { }
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
