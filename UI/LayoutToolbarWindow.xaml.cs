using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using WindowMark.Core;
using WindowMark.Native;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace WindowMark.UI;

public partial class LayoutToolbarWindow : Window
{
    private static readonly Brush NormalPresetBrush = Frozen(Color.FromRgb(0x2A, 0x2A, 0x2A));
    private static readonly Brush SelectedPresetBrush = Frozen(Palette.LayoutAccent);
    private static readonly Brush NormalDotBrush = Frozen(Color.FromRgb(0x7A, 0x7A, 0x7A));

    private readonly Dictionary<LabelAnchor, Button> _positionButtons = new();
    private readonly Dictionary<LabelAnchor, Border> _positionDots = new();
    private readonly Dictionary<double, Button> _sizeButtons = new();
    private OverlayWindow? _overlay;

    internal event Action? Done;
    internal event Action<LabelAnchor>? PositionPresetRequested;
    internal event Action<double>? SizePresetRequested;
    internal event Action? ResetRequested;

    public LayoutToolbarWindow()
    {
        InitializeComponent();

        BuildPositionGrid();
        BuildSizeGrid();

        DoneButton.Click += (_, _) => Done?.Invoke();
        ResetButton.Click += (_, _) => ResetRequested?.Invoke();

        Loaded += (_, _) => PositionNearCurrent();
        DpiChanged += (_, _) => Dispatcher.BeginInvoke(new Action(PositionNearCurrent),
            DispatcherPriority.Loaded);
    }

    internal void ShowFor(OverlayWindow? overlay)
    {
        _overlay = overlay;
        if (overlay == null)
        {
            Hide();
            return;
        }

        if (!IsVisible) Show();
        RefreshSelection();
        UpdateLayout();
        PositionNear(overlay);
    }

    internal void RefreshSelection()
    {
        if (_overlay == null) return;

        var anchor = _overlay.Rule.AnchorValue;
        foreach (var (key, button) in _positionButtons)
        {
            bool selected = key == anchor;
            button.Background = selected ? SelectedPresetBrush : NormalPresetBrush;
            _positionDots[key].Background = selected ? Brushes.White : NormalDotBrush;
        }

        double scale = _overlay.Rule.Scale;
        foreach (var (value, button) in _sizeButtons)
        {
            bool selected = Math.Abs(value - scale) < 0.01;
            button.Background = selected ? SelectedPresetBrush : NormalPresetBrush;
            button.FontWeight = selected ? FontWeights.SemiBold : FontWeights.Normal;
        }

        ScaleText.Text = $"{Math.Round(scale * 100)}%";
    }

    private void BuildPositionGrid()
    {
        foreach (var anchor in Enum.GetValues<LabelAnchor>())
        {
            var dot = new Border
            {
                Width = 7,
                Height = 7,
                CornerRadius = new CornerRadius(3.5),
                Background = NormalDotBrush
            };

            var button = new Button
            {
                Content = dot,
                Style = (Style)FindResource("PresetButton"),
                ToolTip = LabelLayout.AnchorName(anchor)
            };
            button.Click += (_, _) => PositionPresetRequested?.Invoke(anchor);

            PositionGrid.Children.Add(button);
            _positionButtons[anchor] = button;
            _positionDots[anchor] = dot;
        }
    }

    private void BuildSizeGrid()
    {
        foreach (var (label, scale) in LabelLayout.SizePresets)
        {
            var button = new Button
            {
                Content = label,
                Style = (Style)FindResource("PresetButton"),
                ToolTip = $"{scale * 100:0}%"
            };
            button.Click += (_, _) => SizePresetRequested?.Invoke(scale);

            SizeGrid.Children.Add(button);
            _sizeButtons[scale] = button;
        }
    }

    private void PositionNearCurrent()
    {
        if (_overlay != null) PositionNear(_overlay);
    }

    private void PositionNear(OverlayWindow overlay)
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero || !overlay.TryGetScreenRect(out var chip)) return;

        var dpi = VisualTreeHelper.GetDpi(this);
        int width = (int)Math.Ceiling(ActualWidth * dpi.DpiScaleX);
        int height = (int)Math.Ceiling(ActualHeight * dpi.DpiScaleY);
        if (width <= 0 || height <= 0) return;

        int chipWidth = Math.Max(1, chip.Right - chip.Left);
        int chipHeight = Math.Max(1, chip.Bottom - chip.Top);
        var screen = Forms.Screen.FromRectangle(
            new Drawing.Rectangle(chip.Left, chip.Top, chipWidth, chipHeight));
        var area = screen.WorkingArea;

        int x = chip.Left + (chipWidth - width) / 2;
        int y = chip.Top - height - 10;
        if (y < area.Top + 8) y = chip.Bottom + 10;

        x = Math.Clamp(x, area.Left + 8, Math.Max(area.Left + 8, area.Right - width - 8));
        y = Math.Clamp(y, area.Top + 8, Math.Max(area.Top + 8, area.Bottom - height - 8));

        NativeMethods.SetWindowPos(handle, IntPtr.Zero, x, y, 0, 0,
            NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var handle = new WindowInteropHelper(this).Handle;
        long exStyle = NativeMethods.GetWindowLongPtr(handle, NativeMethods.GWL_EXSTYLE).ToInt64();
        exStyle |= NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_NOACTIVATE;
        NativeMethods.SetWindowLongPtr(handle, NativeMethods.GWL_EXSTYLE, new IntPtr(exStyle));
    }

    private static Brush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
