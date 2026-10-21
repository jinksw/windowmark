using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
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

public partial class LabelEditorWindow : Window
{
    private readonly Dictionary<string, Border> _swatches = new(StringComparer.OrdinalIgnoreCase);
    private string _selectedColor = Palette.Colors[0];
    private bool _positioned;

    internal LabelEditorAction Action { get; private set; } = LabelEditorAction.Cancel;
    internal string ResultLabel => LabelBox.Text.Trim();
    internal string ResultColor => _selectedColor;

    internal LabelEditorWindow(TargetWindow target, LabelRule? existing, string defaultColor)
    {
        InitializeComponent();

        TargetText.Text = $"{GetAppName(target)}  ·  {target.NativeTitle}";
        LabelBox.Text = existing?.Label ?? "";
        string initialColor = existing != null && Palette.Colors.Contains(existing.Color, StringComparer.OrdinalIgnoreCase)
            ? existing.Color
            : defaultColor;

        BuildSwatches();
        SelectColor(initialColor);
        DeleteButton.Visibility = existing != null ? Visibility.Visible : Visibility.Collapsed;

        CloseButton.Click += (_, _) => Cancel();
        CancelButton.Click += (_, _) => Cancel();
        SaveButton.Click += (_, _) => Commit();
        DeleteButton.Click += (_, _) =>
        {
            Action = LabelEditorAction.Delete;
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
            var color = Palette.Parse(hex);
            var swatch = new Border
            {
                Width = 26,
                Height = 26,
                CornerRadius = new CornerRadius(13),
                Background = new SolidColorBrush(color),
                BorderThickness = new Thickness(2),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF))
            };
            var button = new Button
            {
                Content = swatch,
                Tag = hex,
                Style = (Style)FindResource("SwatchButton")
            };
            button.Click += (_, _) => SelectColor(hex);
            Swatches.Children.Add(button);
            _swatches[hex] = swatch;
        }
    }

    private void SelectColor(string hex)
    {
        _selectedColor = hex;
        foreach (var (key, swatch) in _swatches)
        {
            bool selected = string.Equals(key, hex, StringComparison.OrdinalIgnoreCase);
            swatch.BorderBrush = new SolidColorBrush(selected
                ? Colors.White
                : Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF));
        }

        var color = Palette.Parse(hex);
        SaveButton.Background = new SolidColorBrush(color);
        SaveButton.Foreground = Palette.TextBrushFor(color);
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
        DialogResult = true;
    }

    private void Cancel()
    {
        Action = LabelEditorAction.Cancel;
        Close();
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
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

    private void OnContentRendered(object? sender, EventArgs e)
    {
        if (_positioned) return;
        _positioned = true;

        var handle = new WindowInteropHelper(this).Handle;
        if (!NativeMethods.GetCursorPos(out var cursor))
        {
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
