using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using WindowMark.Core;
using WindowMark.Native;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace WindowMark.UI;

internal enum LabelEditorAction
{
    Cancel,
    Save,
    Delete
}

internal sealed record LabelPreview(
    string Label,
    string Color,
    string Anchor,
    double OffsetX,
    double OffsetY,
    double Scale);

public partial class LabelEditorWindow : Window
{
    private static readonly Color UnselectedSwatchBorder = Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF);
    private static readonly Brush RainbowSwatch = CreateRainbowBrush();
    private static readonly Brush LayoutPresetBrush = CreateFrozenBrush(Color.FromRgb(0x2A, 0x2A, 0x2A));
    private static readonly Brush LayoutSelectedBrush = CreateFrozenBrush(Palette.LayoutAccent);
    private static readonly Brush LayoutDotBrush = CreateFrozenBrush(Color.FromRgb(0x7A, 0x7A, 0x7A));

    private readonly Dictionary<string, Border> _swatches = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<LabelAnchor, Button> _layoutPositionButtons = new();
    private readonly Dictionary<LabelAnchor, Border> _layoutPositionDots = new();
    private readonly Dictionary<double, Button> _layoutSizeButtons = new();
    private Border _customSwatch = null!;
    private Border _customHole = null!;
    private TextBlock _customPlus = null!;
    private string _selectedColor = Palette.Colors[0];
    private string? _customColor;
    private bool _customSelected;
    private double _hue = 45;
    private double _saturation = 0.65;
    private double _value = 1;
    private bool _suppressHexChange;
    private bool _positioned;
    private readonly Action<LabelPreview>? _preview;
    private readonly DispatcherTimer _previewTimer;
    private bool _imeComposing;
    private bool _previewQueued;
    private LabelPreview? _pendingPreview;
    private bool _closing;
    private LabelAnchor _anchor = LabelAnchor.TopRight;
    private double _offsetX = -LabelLayout.DefaultRightInset;
    private double _offsetY = LabelLayout.DefaultTopInset;
    private double _scale = 1.0;

    internal LabelEditorAction Action { get; private set; } = LabelEditorAction.Cancel;
    internal string ResultLabel => LabelBox.Text.Trim();
    internal string ResultColor => _selectedColor;
    internal string ResultAnchor => _anchor.ToString();
    internal double ResultOffsetX => _offsetX;
    internal double ResultOffsetY => _offsetY;
    internal double ResultScale => _scale;

    internal LabelEditorWindow(
        TargetWindow target,
        LabelRule? existing,
        string defaultColor,
        Action<LabelPreview>? preview = null)
    {
        InitializeComponent();
        _preview = preview;
        _previewTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(200)
        };
        _previewTimer.Tick += OnPreviewTimerTick;

        LoadLayoutState(existing);

        TargetText.Text = $"{GetAppName(target)}  ·  {target.NativeTitle}";
        LabelBox.Text = existing?.Label ?? "";
        LabelBox.AddHandler(TextCompositionManager.TextInputStartEvent,
            new TextCompositionEventHandler(OnImeCompositionStart), true);
        LabelBox.AddHandler(TextCompositionManager.TextInputUpdateEvent,
            new TextCompositionEventHandler(OnImeCompositionUpdate), true);
        LabelBox.AddHandler(TextCompositionManager.TextInputEvent,
            new TextCompositionEventHandler(OnImeCompositionCompleted), true);
        LabelBox.LostKeyboardFocus += OnLabelBoxLostKeyboardFocus;

        BuildSwatches();
        InitializeSelection(existing, defaultColor);
        BuildLayoutControls();
        RefreshLayoutSelection();
        DeleteButton.Visibility = existing != null ? Visibility.Visible : Visibility.Collapsed;

        CloseButton.Click += (_, _) => Cancel();
        CancelButton.Click += (_, _) => Cancel();
        SaveButton.Click += (_, _) => Commit();
        DeleteButton.Click += (_, _) =>
        {
            Action = LabelEditorAction.Delete;
            _closing = true;
            _previewTimer.Stop();
            DialogResult = true;
        };

        HeaderRow.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ButtonState == MouseButtonState.Pressed)
            {
                try { DragMove(); } catch (InvalidOperationException) { }
            }
        };

        PreviewKeyDown += OnPreviewKeyDown;
        PreviewMouseDown += OnWindowPreviewMouseDown;
        ContentRendered += OnContentRendered;
        Loaded += (_, _) => FocusLabelBox();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var handle = new WindowInteropHelper(this).Handle;
        long exStyle = NativeMethods.GetWindowLongPtr(handle, NativeMethods.GWL_EXSTYLE).ToInt64();
        exStyle |= NativeMethods.WS_EX_TOOLWINDOW;
        NativeMethods.SetWindowLongPtr(handle, NativeMethods.GWL_EXSTYLE, new IntPtr(exStyle));
    }

    private static string GetAppName(TargetWindow target)
    {
        if (!string.IsNullOrEmpty(target.ExePath))
        {
            try { return Path.GetFileNameWithoutExtension(target.ExePath); } catch { }
        }
        return target.ClassName;
    }

    private void BuildSwatches()
    {
        foreach (string hex in Palette.Colors)
        {
            var swatch = CreateSwatch();
            swatch.Background = new SolidColorBrush(Palette.Parse(hex));
            var button = new Button
            {
                Content = swatch,
                Tag = hex,
                Style = (Style)FindResource("SwatchButton"),
                ToolTip = hex
            };
            button.Click += (_, _) => SelectBuiltIn(hex);
            Swatches.Children.Add(button);
            _swatches[hex] = swatch;
        }

        _customHole = new Border
        {
            Width = 14,
            Height = 14,
            CornerRadius = new CornerRadius(7),
            Background = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2A)),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        _customPlus = new TextBlock
        {
            Text = "+",
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(0xED, 0xED, 0xED)),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 1)
        };

        _customSwatch = CreateSwatch();
        _customSwatch.Background = RainbowSwatch;
        var content = new Grid();
        content.Children.Add(_customHole);
        content.Children.Add(_customPlus);
        _customSwatch.Child = content;

        var customButton = new Button
        {
            Content = _customSwatch,
            Style = (Style)FindResource("SwatchButton"),
            ToolTip = "自定义颜色"
        };
        customButton.Click += (_, _) => OnCustomSwatchClick();
        Swatches.Children.Add(customButton);
    }

    private static Border CreateSwatch() => new()
    {
        Width = 26,
        Height = 26,
        CornerRadius = new CornerRadius(13),
        BorderThickness = new Thickness(2),
        BorderBrush = new SolidColorBrush(UnselectedSwatchBorder)
    };

    private static Brush CreateFrozenBrush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private static Brush CreateRainbowBrush()
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(1, 1)
        };
        brush.GradientStops.Add(new GradientStop(Color.FromRgb(0xFF, 0x5B, 0x5B), 0));
        brush.GradientStops.Add(new GradientStop(Color.FromRgb(0xFF, 0xD1, 0x66), 0.22));
        brush.GradientStops.Add(new GradientStop(Color.FromRgb(0xA7, 0xE8, 0xA4), 0.42));
        brush.GradientStops.Add(new GradientStop(Color.FromRgb(0x8E, 0xCA, 0xE6), 0.62));
        brush.GradientStops.Add(new GradientStop(Color.FromRgb(0xC9, 0xB6, 0xF5), 0.82));
        brush.GradientStops.Add(new GradientStop(Color.FromRgb(0xF4, 0xA6, 0xC0), 1));
        brush.Freeze();
        return brush;
    }

    private void InitializeSelection(LabelRule? existing, string defaultColor)
    {
        if (existing != null && Palette.TryParse(existing.Color, out var existingColor))
        {
            string hex = Palette.ToHex(existingColor);
            if (Palette.Colors.Contains(hex, StringComparer.OrdinalIgnoreCase))
            {
                SelectBuiltIn(hex);
            }
            else
            {
                _customColor = hex;
                SelectCustom();
            }
            return;
        }

        SelectBuiltIn(defaultColor);
    }

    private void BuildLayoutControls()
    {
        var presetStyle = (Style)FindResource("PresetButton");

        foreach (var anchor in Enum.GetValues<LabelAnchor>())
        {
            var dot = new Border
            {
                Width = 7,
                Height = 7,
                CornerRadius = new CornerRadius(3.5),
                Background = LayoutDotBrush
            };

            var button = new Button
            {
                Content = dot,
                Style = presetStyle,
                ToolTip = LabelLayout.AnchorName(anchor)
            };
            button.Click += (_, _) => SelectAnchor(anchor);

            PositionGrid.Children.Add(button);
            _layoutPositionButtons[anchor] = button;
            _layoutPositionDots[anchor] = dot;
        }

        foreach (var (label, scale) in LabelLayout.SizePresets)
        {
            var button = new Button
            {
                Content = label,
                Style = presetStyle,
                Height = 28,
                ToolTip = $"{scale * 100:0}%"
            };
            button.Click += (_, _) => SelectScale(scale);

            SizeGrid.Children.Add(button);
            _layoutSizeButtons[scale] = button;
        }

        ResetLayoutButton.Click += (_, _) => ResetLayoutSelection();
    }

    private void LoadLayoutState(LabelRule? existing)
    {
        var layout = new LabelRule();
        if (existing != null)
        {
            layout.Anchor = existing.Anchor;
            layout.OffsetX = existing.OffsetX;
            layout.OffsetY = existing.OffsetY;
            layout.Scale = existing.Scale;
        }
        layout.SanitizeLayout();

        _anchor = layout.AnchorValue;
        _offsetX = layout.OffsetX;
        _offsetY = layout.OffsetY;
        _scale = layout.Scale;
    }

    private void SelectAnchor(LabelAnchor anchor)
    {
        var preset = LabelLayout.PositionPreset(anchor);
        _anchor = anchor;
        _offsetX = preset.OffsetX;
        _offsetY = preset.OffsetY;
        RefreshLayoutSelection();
    }

    private void SelectScale(double scale)
    {
        _scale = Math.Clamp(scale, LabelLayout.MinScale, LabelLayout.MaxScale);
        RefreshLayoutSelection();
    }

    private void ResetLayoutSelection()
    {
        _anchor = LabelAnchor.TopRight;
        _offsetX = -LabelLayout.DefaultRightInset;
        _offsetY = LabelLayout.DefaultTopInset;
        _scale = 1.0;
        RefreshLayoutSelection();
    }

    private void RefreshLayoutSelection()
    {
        foreach (var (key, button) in _layoutPositionButtons)
        {
            bool selected = key == _anchor;
            button.Background = selected ? LayoutSelectedBrush : LayoutPresetBrush;
            _layoutPositionDots[key].Background = selected ? Brushes.White : LayoutDotBrush;
        }

        foreach (var (value, button) in _layoutSizeButtons)
        {
            bool selected = Math.Abs(value - _scale) < 0.01;
            button.Background = selected ? LayoutSelectedBrush : LayoutPresetBrush;
            button.FontWeight = selected ? FontWeights.SemiBold : FontWeights.Normal;
        }

        LayoutSummary.Text = $"{Math.Round(_scale * 100)}%";
        QueuePreview();
    }

    private void QueuePreview()
    {
        if (_preview == null || _closing) return;

        _pendingPreview = CreatePreview();
        if (_previewQueued) return;

        _previewQueued = true;
        Dispatcher.BeginInvoke(new Action(() =>
        {
            _previewQueued = false;
            var preview = _pendingPreview;
            _pendingPreview = null;

            if (preview == null || _closing || _imeComposing) return;
            ApplyPreview(preview);
        }), DispatcherPriority.Background);
    }

    private LabelPreview CreatePreview() => new(
        ResultLabel,
        ResultColor,
        ResultAnchor,
        ResultOffsetX,
        ResultOffsetY,
        ResultScale);

    private void ApplyPreview(LabelPreview preview)
    {
        if (_closing || _imeComposing) return;

        try
        {
            _preview?.Invoke(preview);
        }
        catch (Exception ex)
        {
            Log.Error("更新标签预览失败", ex);
        }
    }

    private void OnImeCompositionStart(object sender, TextCompositionEventArgs e)
    {
        _imeComposing = true;
        _previewTimer.Stop();
    }

    private void OnImeCompositionUpdate(object sender, TextCompositionEventArgs e)
    {
        _imeComposing = true;
        _previewTimer.Stop();
    }

    private void OnImeCompositionCompleted(object sender, TextCompositionEventArgs e)
    {
        // TextInput 只在组合输入提交后触发，此时再延迟预览是安全的。
        _imeComposing = false;
        _previewTimer.Stop();
        _previewTimer.Start();
    }

    private void OnLabelBoxLostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        _imeComposing = false;
        _previewTimer.Stop();
        _previewTimer.Start();
    }

    private void OnPreviewTimerTick(object? sender, EventArgs e)
    {
        _previewTimer.Stop();
        if (_imeComposing) return;
        QueuePreview();
    }

    private void SelectBuiltIn(string hex)
    {
        _customSelected = false;
        _selectedColor = hex;
        CustomPanel.Visibility = Visibility.Collapsed;
        RefreshSelectionVisuals();
        UpdateSavePreview(Palette.Parse(hex));
    }

    private void SelectCustom()
    {
        string color = _customColor ?? _selectedColor;
        _customColor = color;
        _customSelected = true;
        _selectedColor = color;
        RefreshSelectionVisuals();
        UpdateCustomSwatchVisual();
        UpdateSavePreview(Palette.Parse(color));
    }

    private void RefreshSelectionVisuals()
    {
        foreach (var (key, swatch) in _swatches)
        {
            bool selected = !_customSelected
                            && string.Equals(key, _selectedColor, StringComparison.OrdinalIgnoreCase);
            swatch.BorderBrush = new SolidColorBrush(selected ? Colors.White : UnselectedSwatchBorder);
        }

        _customSwatch.BorderBrush = new SolidColorBrush(
            _customSelected ? Colors.White : UnselectedSwatchBorder);
    }

    private void UpdateSavePreview(Color color)
    {
        SaveButton.Background = new SolidColorBrush(color);
        SaveButton.Foreground = Palette.TextBrushFor(color);
        QueuePreview();
    }

    private void UpdateCustomSwatchVisual()
    {
        string? custom = _customColor;
        if (custom != null)
        {
            var color = Palette.Parse(custom);
            _customSwatch.Background = new SolidColorBrush(color);
            _customHole.Visibility = Visibility.Collapsed;
            _customPlus.Foreground = Palette.TextBrushFor(color);
            _customPlus.Visibility = Visibility.Visible;
        }
        else
        {
            _customSwatch.Background = RainbowSwatch;
            _customHole.Visibility = Visibility.Visible;
            _customPlus.Foreground = Brushes.White;
            _customPlus.Visibility = Visibility.Visible;
        }
    }

    private void OnCustomSwatchClick()
    {
        if (CustomPanel.Visibility == Visibility.Visible && _customSelected)
        {
            CustomPanel.Visibility = Visibility.Collapsed;
            return;
        }

        string color = _customColor ?? _selectedColor;
        _customColor = color;
        SelectCustom();
        SetHsvFromColor(Palette.Parse(color));

        CustomPanel.Visibility = Visibility.Visible;
        UpdateLayout();
        RefreshPicker();
        Dispatcher.BeginInvoke(new Action(() =>
        {
            SvArea.BringIntoView();
            EnsureOnScreen();
        }), DispatcherPriority.Loaded);
    }

    private void RefreshPicker()
    {
        var color = Palette.Parse(_customColor ?? _selectedColor);
        SvBase.Fill = new SolidColorBrush(HsvToColor(_hue, 1, 1));
        PositionPickerThumbs();
        HexPreview.Background = new SolidColorBrush(color);
        SetHexText(Palette.ToHex(color));
    }

    private void PositionPickerThumbs()
    {
        double width = SvArea.ActualWidth;
        double height = SvArea.ActualHeight;
        if (width <= 0 || height <= 0) return;

        double x = _saturation * width;
        double y = (1 - _value) * height;
        Canvas.SetLeft(SvThumbRing, x - SvThumbRing.Width / 2);
        Canvas.SetTop(SvThumbRing, y - SvThumbRing.Height / 2);
        Canvas.SetLeft(SvThumb, x - SvThumb.Width / 2);
        Canvas.SetTop(SvThumb, y - SvThumb.Height / 2);

        double hueWidth = HueBar.ActualWidth;
        if (hueWidth <= 0) return;
        Canvas.SetLeft(HueThumb, _hue / 360.0 * hueWidth - HueThumb.Width / 2);
        Canvas.SetTop(HueThumb, (HueBar.ActualHeight - HueThumb.Height) / 2);
    }

    private void OnSvMouseDown(object sender, MouseButtonEventArgs e)
    {
        SvArea.CaptureMouse();
        UpdateSaturationValue(e.GetPosition(SvArea));
        e.Handled = true;
    }

    private void OnSvMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed && SvArea.IsMouseCaptured)
        {
            UpdateSaturationValue(e.GetPosition(SvArea));
        }
    }

    private void OnSvMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (SvArea.IsMouseCaptured) SvArea.ReleaseMouseCapture();
    }

    private void UpdateSaturationValue(Point point)
    {
        double width = SvArea.ActualWidth;
        double height = SvArea.ActualHeight;
        if (width <= 0 || height <= 0) return;

        _saturation = Math.Clamp(point.X / width, 0, 1);
        _value = 1 - Math.Clamp(point.Y / height, 0, 1);
        ApplyPickerColor();
    }

    private void OnHueMouseDown(object sender, MouseButtonEventArgs e)
    {
        HueBar.CaptureMouse();
        UpdateHue(e.GetPosition(HueBar));
        e.Handled = true;
    }

    private void OnHueMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed && HueBar.IsMouseCaptured)
        {
            UpdateHue(e.GetPosition(HueBar));
        }
    }

    private void OnHueMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (HueBar.IsMouseCaptured) HueBar.ReleaseMouseCapture();
    }

    private void UpdateHue(Point point)
    {
        double width = HueBar.ActualWidth;
        if (width <= 0) return;

        _hue = Math.Clamp(point.X / width, 0, 1) * 360;
        ApplyPickerColor();
    }

    private void ApplyPickerColor()
    {
        var color = HsvToColor(_hue, _saturation, _value);
        string hex = Palette.ToHex(color);
        _customColor = hex;
        _selectedColor = hex;
        _customSelected = true;

        SvBase.Fill = new SolidColorBrush(HsvToColor(_hue, 1, 1));
        PositionPickerThumbs();
        UpdateCustomSwatchVisual();
        RefreshSelectionVisuals();
        UpdateSavePreview(color);
        HexPreview.Background = new SolidColorBrush(color);
        SetHexText(hex);
    }

    private void OnHexTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressHexChange) return;

        string text = HexBox.Text.Trim();
        if (text.Length == 0) return;
        if (text[0] != '#') text = "#" + text;
        if (text.Length != 7 || !Palette.TryParse(text, out var parsed)) return;

        var color = Color.FromRgb(parsed.R, parsed.G, parsed.B);
        string hex = Palette.ToHex(color);
        _customColor = hex;
        _selectedColor = hex;
        _customSelected = true;
        SetHsvFromColor(color);

        SvBase.Fill = new SolidColorBrush(HsvToColor(_hue, 1, 1));
        PositionPickerThumbs();
        UpdateCustomSwatchVisual();
        RefreshSelectionVisuals();
        UpdateSavePreview(color);
        HexPreview.Background = new SolidColorBrush(color);
    }

    private void OnHexLostFocus(object sender, RoutedEventArgs e)
    {
        SetHexText(Palette.ToHex(Palette.Parse(_customColor ?? _selectedColor)));
    }

    private void SetHexText(string hex)
    {
        if (string.Equals(HexBox.Text, hex, StringComparison.OrdinalIgnoreCase)) return;

        _suppressHexChange = true;
        HexBox.Text = hex;
        _suppressHexChange = false;
    }

    private void OnPickerSizeChanged(object sender, SizeChangedEventArgs e) => PositionPickerThumbs();

    private void SetHsvFromColor(Color color)
    {
        double r = color.R / 255.0;
        double g = color.G / 255.0;
        double b = color.B / 255.0;
        double max = Math.Max(r, Math.Max(g, b));
        double min = Math.Min(r, Math.Min(g, b));
        double delta = max - min;

        _value = max;
        _saturation = max <= 0 ? 0 : delta / max;

        if (delta <= 0) return;
        if (max == r) _hue = 60 * (((g - b) / delta) % 6);
        else if (max == g) _hue = 60 * ((b - r) / delta + 2);
        else _hue = 60 * ((r - g) / delta + 4);
        if (_hue < 0) _hue += 360;
    }

    private static Color HsvToColor(double hue, double saturation, double value)
    {
        hue = ((hue % 360) + 360) % 360;
        saturation = Math.Clamp(saturation, 0, 1);
        value = Math.Clamp(value, 0, 1);

        double c = value * saturation;
        double x = c * (1 - Math.Abs(hue / 60 % 2 - 1));
        double m = value - c;

        (double r, double g, double b) = hue switch
        {
            < 60 => (c, x, 0.0),
            < 120 => (x, c, 0.0),
            < 180 => (0.0, c, x),
            < 240 => (0.0, x, c),
            < 300 => (x, 0.0, c),
            _ => (c, 0.0, x)
        };

        return Color.FromRgb(Channel(r + m), Channel(g + m), Channel(b + m));

        static byte Channel(double channel) => (byte)Math.Round(Math.Clamp(channel, 0, 1) * 255);
    }

    private void EnsureOnScreen()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero) return;
        if (!NativeMethods.GetWindowRect(handle, out var rect)) return;

        int width = rect.Right - rect.Left;
        int height = rect.Bottom - rect.Top;
        if (width <= 0 || height <= 0) return;

        var screen = Forms.Screen.FromRectangle(
            new Drawing.Rectangle(rect.Left, rect.Top, width, height));

        double dpiScale = VisualTreeHelper.GetDpi(this).DpiScaleY;
        double maxHeight = Math.Max(240, screen.WorkingArea.Height / dpiScale - 16);
        if (Math.Abs(MaxHeight - maxHeight) > 0.5)
        {
            MaxHeight = maxHeight;
            UpdateLayout();

            if (!NativeMethods.GetWindowRect(handle, out rect)) return;
            width = rect.Right - rect.Left;
            height = rect.Bottom - rect.Top;
            if (width <= 0 || height <= 0) return;
        }

        var workArea = screen.WorkingArea;
        int x = Math.Clamp(rect.Left, workArea.Left + 8,
            Math.Max(workArea.Left + 8, workArea.Right - width - 8));
        int y = Math.Clamp(rect.Top, workArea.Top + 8,
            Math.Max(workArea.Top + 8, workArea.Bottom - height - 8));

        if (x == rect.Left && y == rect.Top) return;
        NativeMethods.SetWindowPos(handle, IntPtr.Zero, x, y, 0, 0,
            NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);
    }

    private void Commit()
    {
        if (ResultLabel.Length == 0)
        {
            Action = LabelEditorAction.Delete;
        }
        else
        {
            Action = LabelEditorAction.Save;
        }

        _closing = true;
        _previewTimer.Stop();
        DialogResult = true;
    }

    private void Cancel()
    {
        Action = LabelEditorAction.Cancel;
        _closing = true;
        _previewTimer.Stop();
        Close();
    }

    private void OnWindowPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        // WPF/TSF 在组合输入期间点击文本框会走 TextEditor.CompleteComposition，
        // 某些输入法下会直接 FailFast。组合期间先拦截文本框内的鼠标按下。
        if (!_imeComposing || !LabelBox.IsMouseOver) return;
        e.Handled = true;
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        // 输入法组合中，Enter/Escape 属于候选词操作，不能当成保存/关闭。
        if (_imeComposing && (e.Key == Key.Enter || e.Key == Key.Escape))
        {
            if (e.Key == Key.Escape)
            {
                _imeComposing = false;
                _previewTimer.Stop();
                _previewTimer.Start();
            }
            return;
        }

        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            Cancel();
        }
        else if (e.Key == Key.Enter)
        {
            e.Handled = true;
            Commit();
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _closing = true;
        _previewTimer.Stop();
        base.OnClosed(e);
    }

    private void OnContentRendered(object? sender, EventArgs e)
    {
        if (_positioned) return;
        _positioned = true;

        var handle = new WindowInteropHelper(this).Handle;
        if (!NativeMethods.GetCursorPos(out var cursor))
        {
            EnsureOnScreen();
            LabelBox.Focus();
            return;
        }

        var workArea = Forms.Screen.FromPoint(new Drawing.Point(cursor.X, cursor.Y)).WorkingArea;
        NativeMethods.GetWindowRect(handle, out var windowRect);
        int width = windowRect.Right - windowRect.Left;
        int height = windowRect.Bottom - windowRect.Top;

        int x = Math.Clamp(cursor.X + 16, workArea.Left + 8,
            Math.Max(workArea.Left + 8, workArea.Right - width - 8));
        int y = Math.Clamp(cursor.Y + 16, workArea.Top + 8,
            Math.Max(workArea.Top + 8, workArea.Bottom - height - 8));

        NativeMethods.SetWindowPos(handle, IntPtr.Zero, x, y, 0, 0,
            NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);

        EnsureOnScreen();
        FocusLabelBox();
    }

    private void FocusLabelBox()
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (!IsVisible) return;
            Activate();
            LabelBox.Focus();
            Keyboard.Focus(LabelBox);
            LabelBox.SelectAll();
        }), System.Windows.Threading.DispatcherPriority.Input);
    }
}
