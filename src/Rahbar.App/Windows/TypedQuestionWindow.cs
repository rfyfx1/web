using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Rahbar.App.Native;
using Rahbar.Core.Geometry;
using System.Windows.Media.Effects;

namespace Rahbar.App.Windows;

/// <summary>Small box near the cursor for typing a question (Ctrl+Shift+Space). Enter sends, Esc closes.</summary>
internal sealed class TypedQuestionWindow : Window
{
    private readonly TextBox _box;
    private readonly int _x, _y;
    private readonly PixelRect _monitor;

    public string? Question { get; private set; }

    /// <param name="x">Cursor position in physical screen pixels.</param>
    public TypedQuestionWindow(int x, int y, PixelRect monitor)
    {
        _x = x;
        _y = y;
        _monitor = monitor;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Title = "Ask Rahbar";

        _box = new TextBox
        {
            Width = 420,
            FontSize = 15,
            Padding = new Thickness(8, 6, 8, 6),
            BorderThickness = new Thickness(0),
            FontFamily = new FontFamily("Segoe UI, Nirmala UI, Urdu Typesetting"),
            ToolTip = "Type in any language. Enter to ask, Esc to close.",
        };
        var hint = new TextBlock
        {
            Text = "Ask about this screen (any language) · Enter to ask · Esc to close",
            Foreground = new SolidColorBrush(Color.FromRgb(0x6B, 0x72, 0x80)),
            FontSize = 11,
            Margin = new Thickness(10, 0, 10, 6),
        };
        var stack = new StackPanel();
        stack.Children.Add(_box);
        stack.Children.Add(hint);
        Content = new Border
        {
            Background = Brushes.White,
            BorderBrush = (Brush)Application.Current.Resources["AccentBrush"],
            BorderThickness = new Thickness(2),
            CornerRadius = new CornerRadius(10),
            Margin = new Thickness(10),
            Child = stack,
            Effect = new DropShadowEffect { BlurRadius = 16, ShadowDepth = 2, Opacity = 0.35 },
        };

        _box.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter && !string.IsNullOrWhiteSpace(_box.Text))
            {
                Question = _box.Text.Trim();
                Close();
            }
            else if (e.Key == Key.Escape)
            {
                Close();
            }
        };
        Loaded += (_, _) =>
        {
            KeepOnScreen();
            Activate();
            _box.Focus();
            Keyboard.Focus(_box);
        };
        Deactivated += (_, _) => { if (IsVisible) Close(); };
    }

    /// <summary>Places the box just below-right of the cursor, kept inside that monitor (physical pixels).</summary>
    private void KeepOnScreen()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        var dpi = VisualTreeHelper.GetDpi(this).DpiScaleX;
        int w = (int)(ActualWidth * dpi), h = (int)(ActualHeight * dpi);
        int x = Math.Min(_x + 12, _monitor.Right - w - 8);
        int y = Math.Min(_y + 12, _monitor.Bottom - h - 8);
        Win32.SetWindowPos(hwnd, Win32.HWND_TOPMOST, Math.Max(_monitor.Left, x), Math.Max(_monitor.Top, y), 0, 0, 0x0001 /* SWP_NOSIZE */);
    }
}
