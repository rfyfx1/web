using System.Windows.Controls;
using System.Windows.Input;
using Rahbar.Core.Settings;

namespace Rahbar.App.Windows;

/// <summary>A box that records a key combination: click it, then press e.g. Ctrl + Space.</summary>
internal sealed class HotkeyBox : TextBox
{
    private HotkeySettings _hotkey = new();

    public HotkeyBox()
    {
        IsReadOnly = true;
        IsReadOnlyCaretVisible = false;
        FontSize = 15;
        Padding = new System.Windows.Thickness(8, 5, 8, 5);
        ToolTip = "Click here, then press the key combination you want (for example Ctrl + Space).";
        Text = _hotkey.ToString();
    }

    public HotkeySettings Hotkey
    {
        get => _hotkey;
        set { _hotkey = value; Text = value.ToString(); }
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift or Key.LeftAlt or Key.RightAlt
            or Key.LWin or Key.RWin or Key.Escape or Key.Tab)
            return;

        var mods = Keyboard.Modifiers;
        var win = Keyboard.IsKeyDown(Key.LWin) || Keyboard.IsKeyDown(Key.RWin);
        if (mods == ModifierKeys.None && !win)
        {
            Text = "Use Ctrl, Alt or Shift with a key";
            return;
        }

        Hotkey = new HotkeySettings
        {
            Ctrl = mods.HasFlag(ModifierKeys.Control),
            Shift = mods.HasFlag(ModifierKeys.Shift),
            Alt = mods.HasFlag(ModifierKeys.Alt),
            Win = win,
            Key = KeyInterop.VirtualKeyFromKey(key),
        };
    }
}
