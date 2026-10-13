using System.Runtime.InteropServices;
using System.Windows.Threading;
using Rahbar.App.Native;
using Rahbar.Core.Settings;

namespace Rahbar.App.Input;

/// <summary>
/// Global hold-to-talk hotkey using a low-level keyboard hook (RegisterHotKey can't tell us
/// when the key is released). The hotkey's own key presses are swallowed so a held
/// Ctrl+Space doesn't type spaces into the app underneath.
/// </summary>
internal sealed class HotkeyHook : IDisposable
{
    private readonly Dispatcher _ui;
    private readonly Win32.LowLevelProc _proc; // keep a reference so the GC doesn't collect it
    private IntPtr _hook;
    private bool _held;

    public HotkeySettings Hotkey { get; set; }
    public bool Enabled { get; set; } = true;

    public event Action? Pressed;
    public event Action? Released;
    public event Action? Escape;

    public HotkeyHook(HotkeySettings hotkey, Dispatcher ui)
    {
        Hotkey = hotkey;
        _ui = ui;
        _proc = HookProc;
        _hook = Win32.SetWindowsHookEx(Win32.WH_KEYBOARD_LL, _proc, Win32.GetModuleHandle(null), 0);
        if (_hook == IntPtr.Zero)
            throw new InvalidOperationException("Could not install keyboard hook (error " + Marshal.GetLastWin32Error() + ").");
    }

    private IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var info = Marshal.PtrToStructure<Win32.KBDLLHOOKSTRUCT>(lParam);
            var msg = (int)wParam;
            var down = msg is Win32.WM_KEYDOWN or Win32.WM_SYSKEYDOWN;
            var up = msg is Win32.WM_KEYUP or Win32.WM_SYSKEYUP;
            var vk = (int)info.vkCode;
            var injected = (info.flags & Win32.LLKHF_INJECTED) != 0;

            if (down && vk == Win32.VK_ESCAPE)
                _ui.BeginInvoke(() => Escape?.Invoke());

            if (!injected && Enabled)
            {
                if (vk == Hotkey.Key)
                {
                    if (down)
                    {
                        if (_held) return 1; // auto-repeat while held
                        if (ModifiersMatch())
                        {
                            _held = true;
                            _ui.BeginInvoke(() => Pressed?.Invoke());
                            return 1;
                        }
                    }
                    else if (up && _held)
                    {
                        _held = false;
                        _ui.BeginInvoke(() => Released?.Invoke());
                        return 1;
                    }
                }
                else if (up && _held && IsRequiredModifier(vk))
                {
                    // Letting go of Ctrl first also ends the question.
                    _held = false;
                    _ui.BeginInvoke(() => Released?.Invoke());
                }
            }
        }
        return Win32.CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    private bool ModifiersMatch() =>
        Hotkey.Ctrl == IsDown(Win32.VK_CONTROL) &&
        Hotkey.Shift == IsDown(Win32.VK_SHIFT) &&
        Hotkey.Alt == IsDown(Win32.VK_MENU) &&
        Hotkey.Win == (IsDown(Win32.VK_LWIN) || IsDown(Win32.VK_RWIN));

    private bool IsRequiredModifier(int vk) =>
        (Hotkey.Ctrl && vk is Win32.VK_CONTROL or Win32.VK_LCONTROL or Win32.VK_RCONTROL) ||
        (Hotkey.Shift && vk is Win32.VK_SHIFT or Win32.VK_LSHIFT or Win32.VK_RSHIFT) ||
        (Hotkey.Alt && vk is Win32.VK_MENU or Win32.VK_LMENU or Win32.VK_RMENU) ||
        (Hotkey.Win && vk is Win32.VK_LWIN or Win32.VK_RWIN);

    private static bool IsDown(int vk) => (Win32.GetAsyncKeyState(vk) & 0x8000) != 0;

    public void Dispose()
    {
        if (_hook != IntPtr.Zero) Win32.UnhookWindowsHookEx(_hook);
        _hook = IntPtr.Zero;
    }
}
