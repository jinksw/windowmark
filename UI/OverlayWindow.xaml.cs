using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using WindowMark.Core;
using WindowMark.Native;

namespace WindowMark.UI;

public partial class OverlayWindow : Window
{
    private IntPtr _handle;
    private bool _hidden;

    internal TargetWindow Target { get; }
    internal LabelRule Rule { get; private set; }

    internal OverlayWindow(TargetWindow target, LabelRule rule)
    {
        InitializeComponent();
        Target = target;
        Rule = rule;
        ApplyRule(rule);
    }

    internal void ApplyRule(LabelRule rule)
    {
        Rule = rule;
        LabelText.Text = rule.Label;
        var color = Palette.Parse(rule.Color);
        Chip.Background = new SolidColorBrush(color);
        LabelText.Foreground = Palette.TextBrushFor(color);
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _handle = new WindowInteropHelper(this).Handle;

        long exStyle = NativeMethods.GetWindowLongPtr(_handle, NativeMethods.GWL_EXSTYLE).ToInt64();
        exStyle |= NativeMethods.WS_EX_LAYERED
                   | NativeMethods.WS_EX_TRANSPARENT
                   | NativeMethods.WS_EX_NOACTIVATE
                   | NativeMethods.WS_EX_TOOLWINDOW;
        NativeMethods.SetWindowLongPtr(_handle, NativeMethods.GWL_EXSTYLE, new IntPtr(exStyle));
    }

    internal void SyncToTarget()
    {
        if (_handle == IntPtr.Zero) return;

        if (!NativeMethods.IsWindow(Target.Hwnd)
            || !NativeMethods.IsWindowVisible(Target.Hwnd)
            || NativeMethods.IsIconic(Target.Hwnd)
            || WindowService.IsCloaked(Target.Hwnd))
        {
            SetHidden(true);
            return;
        }

        if (!TryGetFrame(Target.Hwnd, out var frame))
        {
            SetHidden(true);
            return;
        }

        var dpi = VisualTreeHelper.GetDpi(this);
        int pixelWidth = (int)Math.Ceiling(ActualWidth * dpi.DpiScaleX);
        if (pixelWidth < 2) return;

        int x = frame.Right - (int)Math.Round(150 * dpi.DpiScaleX) - pixelWidth;
        int minX = frame.Left + (int)Math.Round(6 * dpi.DpiScaleX);
        if (x < minX) x = minX;
        int y = frame.Top + (int)Math.Round(5 * dpi.DpiScaleY);

        SetHidden(false);

        var insertAfter = NativeMethods.GetWindow(Target.Hwnd, NativeMethods.GW_HWNDPREV);
        uint flags = NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_NOOWNERZORDER;
        if (insertAfter == _handle)
        {
            flags |= NativeMethods.SWP_NOZORDER;
            insertAfter = IntPtr.Zero;
        }

        NativeMethods.SetWindowPos(_handle, insertAfter, x, y, 0, 0, flags);
    }

    private static bool TryGetFrame(IntPtr hwnd, out NativeMethods.RECT frame)
    {
        int size = Marshal.SizeOf<NativeMethods.RECT>();
        if (NativeMethods.DwmGetWindowAttribute(hwnd, NativeMethods.DWMWA_EXTENDED_FRAME_BOUNDS, out frame, size) == 0)
            return true;
        return NativeMethods.GetWindowRect(hwnd, out frame);
    }

    private void SetHidden(bool hide)
    {
        if (_hidden == hide) return;
        NativeMethods.ShowWindow(_handle, hide ? NativeMethods.SW_HIDE : NativeMethods.SW_SHOWNOACTIVATE);
        _hidden = hide;
    }
}
