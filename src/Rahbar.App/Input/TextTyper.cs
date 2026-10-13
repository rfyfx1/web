using System.Runtime.InteropServices;
using Rahbar.App.Native;

namespace Rahbar.App.Input;

/// <summary>Types text into whatever has keyboard focus, as Unicode (works for Urdu and Hindi too).</summary>
internal static class TextTyper
{
    public static async Task TypeAsync(string text, CancellationToken ct)
    {
        // Wait (up to 2 s) for Ctrl/Alt/Shift to be released, otherwise letters become shortcuts.
        for (int i = 0; i < 40 && HotkeyHook.AnyModifierDown(); i++) await Task.Delay(50, ct);

        text = text.Replace("\r", "").Replace('\n', ' ');
        var inputs = new List<Win32.INPUT>(text.Length * 2);
        foreach (var ch in text)
        {
            inputs.Add(Key(ch, false));
            inputs.Add(Key(ch, true));
        }

        // Send in small chunks so slow apps keep up.
        const int chunk = 64;
        for (int i = 0; i < inputs.Count; i += chunk)
        {
            ct.ThrowIfCancellationRequested();
            var part = inputs.Skip(i).Take(chunk).ToArray();
            Win32.SendInput((uint)part.Length, part, Marshal.SizeOf<Win32.INPUT>());
            await Task.Delay(5, ct);
        }
    }

    private static Win32.INPUT Key(char ch, bool up) => new()
    {
        type = Win32.INPUT_KEYBOARD,
        u = new Win32.InputUnion
        {
            ki = new Win32.KEYBDINPUT
            {
                wVk = 0,
                wScan = ch,
                dwFlags = Win32.KEYEVENTF_UNICODE | (up ? Win32.KEYEVENTF_KEYUP : 0),
            },
        },
    };
}
