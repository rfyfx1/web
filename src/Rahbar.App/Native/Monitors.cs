using System.Diagnostics;
using System.Text;
using Rahbar.Core.Geometry;

namespace Rahbar.App.Native;

/// <summary>A monitor plus its native handle.</summary>
internal sealed record MonitorHandle(IntPtr Handle, MonitorInfo Info);

internal static class Monitors
{
    public static Win32.POINT CursorPosition()
    {
        Win32.GetCursorPos(out var p);
        return p;
    }

    public static MonitorHandle UnderCursor() => FromPoint(CursorPosition());

    public static MonitorHandle FromPoint(Win32.POINT p) => Describe(Win32.MonitorFromPoint(p, Win32.MONITOR_DEFAULTTONEAREST));

    public static IReadOnlyList<MonitorHandle> All()
    {
        var list = new List<MonitorHandle>();
        Win32.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr h, IntPtr _, ref Win32.RECT _, IntPtr _) =>
        {
            list.Add(Describe(h));
            return true;
        }, IntPtr.Zero);
        return list;
    }

    private static MonitorHandle Describe(IntPtr h)
    {
        var mi = new Win32.MONITORINFOEX { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<Win32.MONITORINFOEX>() };
        Win32.GetMonitorInfo(h, ref mi);
        var r = mi.rcMonitor;
        double scale = 1.0;
        if (Win32.GetDpiForMonitor(h, Win32.MDT_EFFECTIVE_DPI, out var dpiX, out _) == 0)
            scale = MonitorInfo.ScaleFromDpi(dpiX);
        return new MonitorHandle(h, new MonitorInfo(new PixelRect(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top), scale, mi.szDevice ?? ""));
    }
}

internal static class ForegroundApp
{
    public static (string Title, string ProcessName) Get()
    {
        var hwnd = Win32.GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return ("", "");
        var sb = new StringBuilder(512);
        Win32.GetWindowText(hwnd, sb, sb.Capacity);
        string proc = "";
        try
        {
            Win32.GetWindowThreadProcessId(hwnd, out var pid);
            using var p = Process.GetProcessById((int)pid);
            proc = p.ProcessName;
        }
        catch { /* process may have exited or be protected */ }
        return (sb.ToString(), proc);
    }
}
