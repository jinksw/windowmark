using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using WindowMark.Native;

namespace WindowMark.Core;

internal sealed class TrayController : IDisposable
{
    private readonly NotifyIcon _icon;
    private readonly ContextMenuStrip _menu;
    private readonly Icon _iconHandle;
    private readonly IntPtr _nativeIcon;
    private readonly Func<IReadOnlyList<TargetWindow>> _listWindows;
    private readonly Action<IntPtr> _tagWindow;
    private readonly Action _toggleLayoutMode;
    private readonly Action _exit;
    private readonly ToolStripMenuItem _windowListMenu;
    private readonly ToolStripMenuItem _layoutItem;
    private bool _disposed;

    internal TrayController(
        Func<IReadOnlyList<TargetWindow>> listWindows,
        Action<IntPtr> tagWindow,
        Action toggleLayoutMode,
        Action exit)
    {
        _listWindows = listWindows;
        _tagWindow = tagWindow;
        _toggleLayoutMode = toggleLayoutMode;
        _exit = exit;

        (_iconHandle, _nativeIcon) = CreateTrayIcon();

        _menu = new ContextMenuStrip
        {
            ShowImageMargin = false,
            Font = new Font("Microsoft YaHei UI", 9F)
        };

        var hint = new ToolStripMenuItem("Ctrl+Alt+L   标记鼠标下的窗口") { Enabled = false };
        _layoutItem = new ToolStripMenuItem("调整标签位置与大小");
        _layoutItem.Click += (_, _) => _toggleLayoutMode();
        _windowListMenu = new ToolStripMenuItem("选择窗口打标签");
        _windowListMenu.DropDownOpening += (_, _) => FillWindowList();

        var dataFolderItem = new ToolStripMenuItem("打开标签数据文件夹");
        dataFolderItem.Click += (_, _) => OpenDataFolder();

        var exitItem = new ToolStripMenuItem("退出");
        exitItem.Click += (_, _) => _exit();

        _menu.Items.Add(hint);
        _menu.Items.Add(_layoutItem);
        _menu.Items.Add(_windowListMenu);
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(dataFolderItem);
        _menu.Items.Add(exitItem);

        _icon = new NotifyIcon
        {
            Icon = _iconHandle,
            Text = "WindowMark · Ctrl+Alt+L 给窗口打标签",
            Visible = true,
            ContextMenuStrip = _menu
        };
        _icon.DoubleClick += (_, _) =>
            ShowInfo("按 Ctrl+Alt+L 标记鼠标下的窗口，也可以右键托盘图标选择窗口。");
    }

    internal void ShowStartup(bool hotkeyRegistered)
    {
        ShowInfo(hotkeyRegistered
            ? "已启动。按 Ctrl+Alt+L 标记鼠标下的窗口。"
            : "热键 Ctrl+Alt+L 被占用，请右键托盘图标选择窗口。");
    }

    internal void SetLayoutModeActive(bool active)
    {
        if (_disposed) return;
        _layoutItem.Text = active ? "退出调整模式" : "调整标签位置与大小";
    }

    internal void ShowInfo(string text)
    {
        if (_disposed) return;
        _icon.ShowBalloonTip(2500, "WindowMark", text, ToolTipIcon.Info);
    }

    private void FillWindowList()
    {
        _windowListMenu.DropDownItems.Clear();

        List<TargetWindow> windows;
        try
        {
            windows = _listWindows().ToList();
        }
        catch (Exception ex)
        {
            Log.Error("枚举窗口失败", ex);
            windows = new List<TargetWindow>();
        }

        if (windows.Count == 0)
        {
            _windowListMenu.DropDownItems.Add(new ToolStripMenuItem("（没有可标记的窗口）") { Enabled = false });
            return;
        }

        foreach (var window in windows)
        {
            string app = string.IsNullOrEmpty(window.ExePath)
                ? window.ClassName
                : Path.GetFileNameWithoutExtension(window.ExePath);
            string text = $"{app}  ·  {window.NativeTitle}";
            if (text.Length > 80) text = text[..77] + "…";

            var item = new ToolStripMenuItem(text) { Tag = window.Hwnd, ToolTipText = window.NativeTitle };
            item.Click += (_, _) =>
            {
                if (item.Tag is IntPtr hwnd && NativeMethods.IsWindow(hwnd)) _tagWindow(hwnd);
            };
            _windowListMenu.DropDownItems.Add(item);
        }
    }

    private static void OpenDataFolder()
    {
        try
        {
            Directory.CreateDirectory(LabelStore.DataDirectory);
            Process.Start(new ProcessStartInfo
            {
                FileName = LabelStore.DataDirectory,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            Log.Error("打开数据文件夹失败", ex);
        }
    }

    private static (Icon icon, IntPtr nativeHandle) CreateTrayIcon()
    {
        using var bitmap = new Bitmap(32, 32);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.Clear(Color.Transparent);

            using var path = RoundedRect(new RectangleF(2, 2, 28, 28), 9f);
            using var background = new SolidBrush(Color.FromArgb(255, 255, 209, 102));
            graphics.FillPath(background, path);

            using var font = new Font("Segoe UI", 17, FontStyle.Bold, GraphicsUnit.Pixel);
            using var format = new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center
            };
            graphics.DrawString("W", font, Brushes.Black, new RectangleF(0, 0, 32, 33), format);
        }

        IntPtr handle = bitmap.GetHicon();
        return (Icon.FromHandle(handle), handle);
    }

    private static GraphicsPath RoundedRect(RectangleF rect, float radius)
    {
        float diameter = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(rect.X, rect.Y, diameter, diameter, 180, 90);
        path.AddArc(rect.Right - diameter, rect.Y, diameter, diameter, 270, 90);
        path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(rect.X, rect.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _icon.Visible = false;
        _icon.Dispose();
        _menu.Dispose();
        _iconHandle.Dispose();
        if (_nativeIcon != IntPtr.Zero) NativeMethods.DestroyIcon(_nativeIcon);
    }
}
