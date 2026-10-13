using System.Runtime.InteropServices;
using System.Windows.Threading;
using Rahbar.App.Native;
using Rahbar.Core.Settings;

namespace Rahbar.App.Input;

public enum HotkeyId { Ask, Typed, Dictate }

/// <summary>
/// Global hotkeys using a low-level keyboard hook (RegisterHotKey can't tell us when a key is
/// released, which hold-to-talk needs). The hotkey's own key presses are swallowed so a held
/// Ctrl+Space doesn't type spaces into the app underneath.
/// </summary>
internal sealed class HotkeyHook : IDisposable
{
    private sealed record Binding(HotkeyId Id, HotkeySettings Keys);

    private readonly Dispatcher _ui;
    private readonly Win32.LowLevelProc _proc; // keep a reference so the GC doesn't collect it
    private IntPtr _hook;
    private Binding[] _bindings = Array.Empty<Binding>();
    private Binding? _held;

    public bool Enabled { get; set; } = true;

    public event Action<HotkeyId>? Pressed;
    public event Action<HotkeyId>? Released;
    public event Action? Escape;

    public HotkeyHook(AppSettings settings, Dispatcher ui)
    {
        _ui = ui;
        Update(settings);
        _proc = HookProc;
        _hook = Win32.SetWindowsHookEx(Win32.WH_KEYBOARD_LL, _proc, Win32.GetModuleHandle(null), 0);
        if (_hook == IntPtr.Zero)
            throw new InvalidOperationException("Could not install keyboard hook (error " + Marshal.GetLastWin32Error() + ").");
    }

    public void Update(AppSettings s) => _bindings = new[]
    {
        new Binding(HotkeyId.Ask, s.Hotkey),
        new Binding(HotkeyId.Typed, s.TypedHotkey),
        new Binding(HotkeyId.Dictate, s.DictationHotkey),
    };

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

            if (down && vk == Win32.VK_ESCAPE && !injected)
                _ui.BeginInvoke(() => Escape?.Invoke());

            if (!injected && Enabled)
            {
                if (_held is { } held)
                {
                    if (vk == held.Keys.Key)
                    {
                        if (down) return 1; // auto-repeat while held
                        if (up)
                        {
                            _held = null;
                            _ui.BeginInvoke(() => Released?.Invoke(held.Id));
                            return 1;
                        }
                    }
                    else if (up && IsRequiredModifier(held.Keys, vk))
                    {
                        // Letting go of Ctrl first also ends it.
                        _held = null;
                        _ui.BeginInvoke(() => Released?.Invoke(held.Id));
                    }
                }
                else if (down)
                {
                    foreach (var b in _bindings)
                    {
                        if (b.Keys.Key != vk || !ModifiersMatch(b.Keys)) continue;
                        _held = b;
                        _ui.BeginInvoke(() => Pressed?.Invoke(b.Id));
                        return 1;
                    }
                }
            }
        }
        return Win32.CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    private static bool ModifiersMatch(HotkeySettings k) =>
        k.Ctrl == IsDown(Win32.VK_CONTROL) &&
        k.Shift == IsDown(Win32.VK_SHIFT) &&
        k.Alt == IsDown(Win32.VK_MENU) &&
        k.Win == (IsDown(Win32.VK_LWIN) || IsDown(Win32.VK_RWIN));

    private static bool IsRequiredModifier(HotkeySettings k, int vk) =>
        (k.Ctrl && vk is Win32.VK_CONTROL or Win32.VK_LCONTROL or Win32.VK_RCONTROL) ||
        (k.Shift && vk is Win32.VK_SHIFT or Win32.VK_LSHIFT or Win32.VK_RSHIFT) ||
        (k.Alt && vk is Win32.VK_MENU or Win32.VK_LMENU or Win32.VK_RMENU) ||
        (k.Win && vk is Win32.VK_LWIN or Win32.VK_RWIN);

    private static bool IsDown(int vk) => (Win32.GetAsyncKeyState(vk) & 0x8000) != 0;

    /// <summary>True while any of Ctrl/Shift/Alt/Win is physically held.</summary>
    public static bool AnyModifierDown() =>
        IsDown(Win32.VK_CONTROL) || IsDown(Win32.VK_SHIFT) || IsDown(Win32.VK_MENU) || IsDown(Win32.VK_LWIN) || IsDown(Win32.VK_RWIN);

    public void Dispose()
    {
        if (_hook != IntPtr.Zero) Win32.UnhookWindowsHookEx(_hook);
        _hook = IntPtr.Zero;
    }
}
