using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using WindowMark.Core;
using WindowMark.Native;

namespace WindowMark.UI;

public partial class OverlayWindow : Window
{
    private const double BaseFontSize = 12;
    private const double BaseMaxWidth = 220;
    private const double BaseCornerRadius = 7;
    private const double MoveThreshold = 2;

    private static readonly Brush NormalBorderBrush = Frozen(Color.FromArgb(0x30, 0, 0, 0));
    private static readonly Brush LayoutBorderBrush = Frozen(Palette.LayoutAccent);
    private static readonly Brush LayoutBorderDimBrush = Frozen(Color.FromArgb(0x99, 0x4C, 0x9A, 0xFF));

    private IntPtr _handle;
    private bool _hidden;
    private bool _layoutMode;
    private bool _selected;
    private bool _dragging;
    private bool _resizing;
    private bool _dragMoved;

    private NativeMethods.POINT _dragStartCursor;
    private double _dragStartOffsetX;
    private double _dragStartOffsetY;
    private double _dragStartScale;
    private double _dragStartWidth;
    private double _dragStartHeight;
    private int _dragStartRight;
    private int _dragStartBottom;

    internal TargetWindow Target { get; }
    internal LabelRule Rule { get; private set; }
    internal bool IsShown => !_hidden;
    internal bool IsAdjusting => _dragging;

    internal event EventHandler? Selected;
    internal event EventHandler? LayoutChanged;

    internal OverlayWindow(TargetWindow target, LabelRule rule)
    {
        InitializeComponent();
        Target = target;
        Rule = rule.Clone();
        ApplyRuleVisuals();

        Chip.MouseLeftButtonDown += OnChipMouseDown;
        ResizeGrip.MouseLeftButtonDown += OnGripMouseDown;
        RootGrid.MouseMove += OnRootMouseMove;
        RootGrid.MouseLeftButtonUp += OnRootMouseUp;
        RootGrid.LostMouseCapture += OnRootLostMouseCapture;
    }

    internal void ApplyRule(LabelRule rule)
    {
        Rule = rule.Clone();
        ApplyRuleVisuals();

        if (_handle == IntPtr.Zero) return;
        UpdateLayout();
        SyncToTarget();
    }

    internal void SetLayoutMode(bool enabled)
    {
        if (_layoutMode == enabled) return;

        _layoutMode = enabled;
        _selected = false;

        if (_handle != IntPtr.Zero) SetClickThrough(!enabled);
        UpdateChipChrome();

        if (_handle == IntPtr.Zero) return;
        UpdateLayout();
        SyncToTarget();
    }

    internal void SetSelected(bool selected)
    {
        if (_selected == selected) return;

        _selected = selected;
        UpdateChipChrome();
    }

    internal bool TryGetScreenRect(out NativeMethods.RECT rect)
    {
        rect = default;
        if (_handle == IntPtr.Zero || _hidden) return false;
        return NativeMethods.GetWindowRect(_handle, out rect);
    }

    private static Brush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private void ApplyRuleVisuals()
    {
        LabelText.Text = Rule.Label;

        var color = Palette.Parse(Rule.Color);
        Chip.Background = new SolidColorBrush(color);
        LabelText.Foreground = Palette.TextBrushFor(color);
        GripPath.Stroke = LabelText.Foreground;

        ApplyScale();
        UpdateChipChrome();
    }

    private void ApplyScale()
    {
        double scale = Math.Clamp(Rule.Scale, LabelLayout.MinScale, LabelLayout.MaxScale);
        LabelText.FontSize = BaseFontSize * scale;
        LabelText.MaxWidth = BaseMaxWidth * scale;
        Chip.CornerRadius = new CornerRadius(BaseCornerRadius * scale);
        UpdateChipPadding();
    }

    private void UpdateChipPadding()
    {
        double scale = Math.Clamp(Rule.Scale, LabelLayout.MinScale, LabelLayout.MaxScale);
        // 调整模式在右侧额外留出固定 14px 放缩放手柄，避免压住文字。
        double right = 10 * scale + (_layoutMode ? 14 : 0);
        Chip.Padding = new Thickness(9 * scale, 2 * scale, right, 3 * scale);
    }

    private void UpdateChipChrome()
    {
        if (_layoutMode)
        {
            Chip.BorderBrush = _selected ? LayoutBorderBrush : LayoutBorderDimBrush;
            Chip.BorderThickness = new Thickness(1.5);
            Chip.Cursor = Cursors.SizeAll;
            ResizeGrip.Visibility = _selected ? Visibility.Visible : Visibility.Collapsed;
        }
        else
        {
            Chip.BorderBrush = NormalBorderBrush;
            Chip.BorderThickness = new Thickness(1);
            Chip.Cursor = Cursors.Arrow;
            ResizeGrip.Visibility = Visibility.Collapsed;
        }

        UpdateChipPadding();
    }

    private void OnChipMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (!_layoutMode) return;

        Selected?.Invoke(this, EventArgs.Empty);
        if (!NativeMethods.GetCursorPos(out _dragStartCursor)) return;

        _dragStartOffsetX = Rule.OffsetX;
        _dragStartOffsetY = Rule.OffsetY;
        _resizing = false;
        _dragMoved = false;
        _dragging = true;
        RootGrid.CaptureMouse();
        e.Handled = true;
    }

    private void OnGripMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (!_layoutMode || _handle == IntPtr.Zero) return;

        Selected?.Invoke(this, EventArgs.Empty);
        if (!NativeMethods.GetCursorPos(out _dragStartCursor)) return;
        if (!NativeMethods.GetWindowRect(_handle, out var rect)) return;

        _dragStartScale = Rule.Scale;
        _dragStartWidth = ActualWidth;
        _dragStartHeight = ActualHeight;
        _dragStartRight = rect.Right;
        _dragStartBottom = rect.Bottom;
        _resizing = true;
        _dragMoved = false;
        _dragging = true;
        RootGrid.CaptureMouse();
        e.Handled = true;
    }

    private void OnRootMouseMove(object sender, MouseEventArgs e)
    {
        if (!_dragging || e.LeftButton != MouseButtonState.Pressed) return;
        if (!NativeMethods.GetCursorPos(out var cursor)) return;

        var dpi = VisualTreeHelper.GetDpi(this);
        double dx = (cursor.X - _dragStartCursor.X) / dpi.DpiScaleX;
        double dy = (cursor.Y - _dragStartCursor.Y) / dpi.DpiScaleY;

        if (_resizing)
        {
            ResizeTo(cursor, dx, dy, dpi);
        }
        else
        {
            MoveTo(dx, dy);
        }
    }

    private void MoveTo(double dx, double dy)
    {
        Rule.OffsetX = _dragStartOffsetX + dx;
        Rule.OffsetY = _dragStartOffsetY + dy;

        if (Math.Abs(dx) > MoveThreshold || Math.Abs(dy) > MoveThreshold)
            _dragMoved = true;

        SyncToTarget();
    }

    private void ResizeTo(NativeMethods.POINT cursor, double dx, double dy, DpiScale dpi)
    {
        double width = Math.Max(1, _dragStartWidth);
        double height = Math.Max(1, _dragStartHeight);
        double ratioX = dx / width;
        double ratioY = dy / height;
        double ratio = Math.Abs(ratioX) >= Math.Abs(ratioY) ? ratioX : ratioY;
        double scale = Math.Clamp(_dragStartScale * (1 + ratio),
            LabelLayout.MinScale, LabelLayout.MaxScale);

        if (Math.Abs(scale - Rule.Scale) < 0.001) return;

        Rule.Scale = scale;
        ApplyScale();
        UpdateLayout();

        if (!TryGetFrame(Target.Hwnd, out var frame)) return;

        int pixelWidth = (int)Math.Ceiling(ActualWidth * dpi.DpiScaleX);
        int pixelHeight = (int)Math.Ceiling(ActualHeight * dpi.DpiScaleY);
        if (pixelWidth < 2 || pixelHeight < 2) return;

        int desiredRight = _dragStartRight + (cursor.X - _dragStartCursor.X);
        int desiredBottom = _dragStartBottom + (cursor.Y - _dragStartCursor.Y);
        int desiredLeft = desiredRight - pixelWidth;
        int desiredTop = desiredBottom - pixelHeight;

        var (ax, ay) = GetAnchorFactors(Rule.AnchorValue);
        double baseX = frame.Left + ax * (frame.Right - frame.Left - pixelWidth);
        double baseY = frame.Top + ay * (frame.Bottom - frame.Top - pixelHeight);

        Rule.OffsetX = (desiredLeft - baseX) / dpi.DpiScaleX;
        Rule.OffsetY = (desiredTop - baseY) / dpi.DpiScaleY;
        _dragMoved = true;

        SyncToTarget();
    }

    private void OnRootMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_dragging) return;

        bool moved = _dragMoved;
        _dragging = false;
        _resizing = false;

        if (RootGrid.IsMouseCaptured) RootGrid.ReleaseMouseCapture();
        if (moved) CommitLayout();
        e.Handled = true;
    }

    private void OnRootLostMouseCapture(object sender, MouseEventArgs e)
    {
        if (!_dragging) return;

        bool moved = _dragMoved;
        _dragging = false;
        _resizing = false;

        if (moved) CommitLayout();
    }

    private void CommitLayout()
    {
        if (_handle == IntPtr.Zero) return;
        if (!TryGetFrame(Target.Hwnd, out var frame)) return;
        if (!NativeMethods.GetWindowRect(_handle, out var rect)) return;

        int frameWidth = frame.Right - frame.Left;
        int frameHeight = frame.Bottom - frame.Top;
        if (frameWidth <= 0 || frameHeight <= 0) return;

        int width = rect.Right - rect.Left;
        int height = rect.Bottom - rect.Top;
        if (width <= 0 || height <= 0) return;

        double fx = (rect.Left + width / 2.0 - frame.Left) / frameWidth;
        double fy = (rect.Top + height / 2.0 - frame.Top) / frameHeight;
        var anchor = AnchorFromFractions(fx, fy);
        var (ax, ay) = GetAnchorFactors(anchor);

        var dpi = VisualTreeHelper.GetDpi(this);
        double baseX = frame.Left + ax * (frameWidth - width);
        double baseY = frame.Top + ay * (frameHeight - height);

        Rule.Anchor = anchor.ToString();
        Rule.OffsetX = (rect.Left - baseX) / dpi.DpiScaleX;
        Rule.OffsetY = (rect.Top - baseY) / dpi.DpiScaleY;
        Rule.Scale = Math.Clamp(Rule.Scale, LabelLayout.MinScale, LabelLayout.MaxScale);

        LayoutChanged?.Invoke(this, EventArgs.Empty);
        SyncToTarget();
    }

    private static (double X, double Y) GetAnchorFactors(LabelAnchor anchor) => anchor switch
    {
        LabelAnchor.TopLeft => (0, 0),
        LabelAnchor.TopCenter => (0.5, 0),
        LabelAnchor.TopRight => (1, 0),
        LabelAnchor.MiddleLeft => (0, 0.5),
        LabelAnchor.Center => (0.5, 0.5),
        LabelAnchor.MiddleRight => (1, 0.5),
        LabelAnchor.BottomLeft => (0, 1),
        LabelAnchor.BottomCenter => (0.5, 1),
        LabelAnchor.BottomRight => (1, 1),
        _ => (1, 0)
    };

    private static LabelAnchor AnchorFromFractions(double fx, double fy)
    {
        int column = fx < 1.0 / 3 ? 0 : fx < 2.0 / 3 ? 1 : 2;
        int row = fy < 1.0 / 3 ? 0 : fy < 2.0 / 3 ? 1 : 2;
        return (LabelAnchor)(row * 3 + column);
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _handle = new WindowInteropHelper(this).Handle;

        long exStyle = NativeMethods.GetWindowLongPtr(_handle, NativeMethods.GWL_EXSTYLE).ToInt64();
        exStyle |= NativeMethods.WS_EX_LAYERED
                   | NativeMethods.WS_EX_NOACTIVATE
                   | NativeMethods.WS_EX_TOOLWINDOW;
        if (!_layoutMode) exStyle |= NativeMethods.WS_EX_TRANSPARENT;
        NativeMethods.SetWindowLongPtr(_handle, NativeMethods.GWL_EXSTYLE, new IntPtr(exStyle));
    }

    private void SetClickThrough(bool clickThrough)
    {
        if (_handle == IntPtr.Zero) return;

        long exStyle = NativeMethods.GetWindowLongPtr(_handle, NativeMethods.GWL_EXSTYLE).ToInt64();
        if (clickThrough) exStyle |= NativeMethods.WS_EX_TRANSPARENT;
        else exStyle &= ~NativeMethods.WS_EX_TRANSPARENT;
        NativeMethods.SetWindowLongPtr(_handle, NativeMethods.GWL_EXSTYLE, new IntPtr(exStyle));

        NativeMethods.SetWindowPos(_handle, IntPtr.Zero, 0, 0, 0, 0,
            NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOZORDER
            | NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_FRAMECHANGED);
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

        int frameWidth = frame.Right - frame.Left;
        int frameHeight = frame.Bottom - frame.Top;
        if (frameWidth <= 0 || frameHeight <= 0)
        {
            SetHidden(true);
            return;
        }

        var dpi = VisualTreeHelper.GetDpi(this);
        int pixelWidth = (int)Math.Ceiling(ActualWidth * dpi.DpiScaleX);
        int pixelHeight = (int)Math.Ceiling(ActualHeight * dpi.DpiScaleY);
        if (pixelWidth < 2 || pixelHeight < 2) return;

        var (ax, ay) = GetAnchorFactors(Rule.AnchorValue);
        double baseX = frame.Left + ax * (frameWidth - pixelWidth);
        double baseY = frame.Top + ay * (frameHeight - pixelHeight);
        int x = (int)Math.Round(baseX + Rule.OffsetX * dpi.DpiScaleX);
        int y = (int)Math.Round(baseY + Rule.OffsetY * dpi.DpiScaleY);

        int marginX = (int)Math.Round(6 * dpi.DpiScaleX);
        int marginY = (int)Math.Round(4 * dpi.DpiScaleY);
        int minX = frame.Left + marginX;
        int maxX = Math.Max(minX, frame.Right - pixelWidth - marginX);
        int minY = frame.Top + marginY;
        int maxY = Math.Max(minY, frame.Bottom - pixelHeight - marginY);
        x = Math.Clamp(x, minX, maxX);
        y = Math.Clamp(y, minY, maxY);

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
