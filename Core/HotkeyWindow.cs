using System;
using System.Windows.Forms;
using WindowMark.Native;

namespace WindowMark.Core;

internal sealed class HotkeyWindow : NativeWindow, IDisposable
{
    private const int WmHotkey = 0x0312;
    private const int HotkeyId = 1;
    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;
    private const uint ModNoRepeat = 0x4000;

    public bool Registered { get; }
    public event EventHandler? Pressed;

    public HotkeyWindow()
    {
        CreateHandle(new CreateParams { Caption = "WindowMark.Hotkey" });
        Registered = NativeMethods.RegisterHotKey(Handle, HotkeyId,
            ModControl | ModAlt | ModNoRepeat, (uint)Keys.L);
    }

    protected override void WndProc(ref Message message)
    {
        if (message.Msg == WmHotkey && message.WParam.ToInt32() == HotkeyId)
        {
            Pressed?.Invoke(this, EventArgs.Empty);
            return;
        }
        base.WndProc(ref message);
    }

    public void Dispose()
    {
        if (Handle == IntPtr.Zero) return;
        NativeMethods.UnregisterHotKey(Handle, HotkeyId);
        DestroyHandle();
    }
}
