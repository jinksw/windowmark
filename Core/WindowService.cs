using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using WindowMark.Native;

namespace WindowMark.Core;

internal sealed record TargetWindow(IntPtr Hwnd, uint ProcessId, string ExePath, string ClassName, string NativeTitle);

internal static class WindowService
{
    private static readonly HashSet<string> ExcludedClasses = new(StringComparer.OrdinalIgnoreCase)
    {
        "Shell_TrayWnd",
        "Shell_SecondaryTrayWnd",
        "Progman",
        "WorkerW",
        "DV2ControlHost",
        "Windows.UI.Core.CoreWindow",
        "ApplicationManager_DesktopShellWindow",
        "SysShadow",
        "ForegroundStaging",
        "MultitaskingViewFrame",
        "TaskListThumbnailWnd",
        "XamlExplorerHostIslandWindow",
        "Windows.UI.Composition.DesktopWindowContentBridge"
    };

    private static readonly int OwnProcessId = Environment.ProcessId;

    public static TargetWindow? Describe(IntPtr hwnd, bool requireTitle = false)
    {
        if (hwnd == IntPtr.Zero || !NativeMethods.IsWindow(hwnd)) return null;

        var root = NativeMethods.GetAncestor(hwnd, NativeMethods.GA_ROOT);
        if (root != IntPtr.Zero) hwnd = root;

        if (!NativeMethods.IsWindowVisible(hwnd) || NativeMethods.IsIconic(hwnd)) return null;

        NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
        if (pid == OwnProcessId) return null;

        long exStyle = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE).ToInt64();
        if ((exStyle & NativeMethods.WS_EX_TOOLWINDOW) != 0) return null;

        string className = GetClassName(hwnd);
        if (ExcludedClasses.Contains(className)) return null;
        if (IsCloaked(hwnd)) return null;

        string title = GetWindowText(hwnd).Trim();
        if (requireTitle && title.Length == 0) return null;
        if (!HasVisibleArea(hwnd)) return null;

        return new TargetWindow(hwnd, pid, GetExePath(pid), className, title);
    }

    public static IntPtr GetWindowUnderCursor()
    {
        if (!NativeMethods.GetCursorPos(out var point)) return IntPtr.Zero;

        var hwnd = NativeMethods.WindowFromPoint(point);
        for (int i = 0; i < 12 && hwnd != IntPtr.Zero; i++)
        {
            var root = NativeMethods.GetAncestor(hwnd, NativeMethods.GA_ROOT);
            if (root == IntPtr.Zero) return IntPtr.Zero;
            if (Describe(root) != null) return root;
            hwnd = NativeMethods.GetWindow(root, NativeMethods.GW_HWNDNEXT);
        }
        return IntPtr.Zero;
    }

    public static IEnumerable<IntPtr> EnumerateTopLevelWindows()
    {
        var result = new List<IntPtr>();
        NativeMethods.EnumWindows((hwnd, _) => { result.Add(hwnd); return true; }, IntPtr.Zero);
        return result;
    }

    public static List<TargetWindow> ListCandidates(int max = 60)
    {
        return EnumerateTopLevelWindows()
            .Select(hwnd => Describe(hwnd, requireTitle: true))
            .Where(target => target != null)
            .Select(target => target!)
            .GroupBy(target => target.Hwnd)
            .Select(group => group.First())
            .OrderBy(target => target.ExePath, StringComparer.OrdinalIgnoreCase)
            .ThenBy(target => target.NativeTitle, StringComparer.CurrentCultureIgnoreCase)
            .Take(max)
            .ToList();
    }

    public static bool IsCloaked(IntPtr hwnd)
    {
        if (NativeMethods.DwmGetWindowAttribute(hwnd, NativeMethods.DWMWA_CLOAKED, out int cloaked, sizeof(int)) != 0)
            return false;
        return cloaked != 0;
    }

    private static bool HasVisibleArea(IntPtr hwnd)
    {
        int size = Marshal.SizeOf<NativeMethods.RECT>();
        if (NativeMethods.DwmGetWindowAttribute(hwnd, NativeMethods.DWMWA_EXTENDED_FRAME_BOUNDS, out NativeMethods.RECT rect, size) != 0)
        {
            if (!NativeMethods.GetWindowRect(hwnd, out rect)) return false;
        }
        return rect.Right - rect.Left > 0 && rect.Bottom - rect.Top > 0;
    }

    private static string GetWindowText(IntPtr hwnd)
    {
        int length = NativeMethods.GetWindowTextLength(hwnd);
        if (length <= 0) return "";
        var buffer = new StringBuilder(length + 1);
        NativeMethods.GetWindowText(hwnd, buffer, buffer.Capacity);
        return buffer.ToString();
    }

    private static string GetClassName(IntPtr hwnd)
    {
        var buffer = new StringBuilder(256);
        NativeMethods.GetClassName(hwnd, buffer, buffer.Capacity);
        return buffer.ToString();
    }

    private static string GetExePath(uint pid)
    {
        IntPtr process = NativeMethods.OpenProcess(NativeMethods.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (process == IntPtr.Zero) return "";

        try
        {
            var buffer = new StringBuilder(1024);
            uint size = (uint)buffer.Capacity;
            if (!NativeMethods.QueryFullProcessImageName(process, 0, buffer, ref size)) return "";
            return buffer.ToString(0, (int)size);
        }
        catch
        {
            return "";
        }
        finally
        {
            NativeMethods.CloseHandle(process);
        }
    }
}
