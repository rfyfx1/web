using System.Runtime.InteropServices;
using System.Windows.Threading;
using Rahbar.App.Native;

namespace Rahbar.App.Input;

/// <summary>
/// Watches left clicks anywhere (without blocking them) while a walkthrough waits, so clicking
/// the highlighted button can move to the next step. Installed only while needed.
/// </summary>
internal sealed class MouseClickHook : IDisposable
{
    private readonly Dispatcher _ui;
    private readonly Win32.LowLevelProc _proc;
    private IntPtr _hook;

    public event Action<int, int>? LeftClick;

    public MouseClickHook(Dispatcher ui)
    {
        _ui = ui;
        _proc = HookProc;
    }

    public bool Active => _hook != IntPtr.Zero;

    public void Start()
    {
        if (_hook == IntPtr.Zero)
            _hook = Win32.SetWindowsHookEx(Win32.WH_MOUSE_LL, _proc, Win32.GetModuleHandle(null), 0);
    }

    public void Stop()
    {
        if (_hook != IntPtr.Zero) Win32.UnhookWindowsHookEx(_hook);
        _hook = IntPtr.Zero;
    }

    private IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && (int)wParam == Win32.WM_LBUTTONDOWN)
        {
            var info = Marshal.PtrToStructure<Win32.MSLLHOOKSTRUCT>(lParam);
            var (x, y) = (info.pt.X, info.pt.Y);
            _ui.BeginInvoke(() => LeftClick?.Invoke(x, y));
        }
        return Win32.CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    public void Dispose() => Stop();
}
