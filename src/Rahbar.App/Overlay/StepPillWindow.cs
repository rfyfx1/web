using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using Rahbar.App.Native;

namespace Rahbar.App.Overlay;

/// <summary>
/// Small floating "‹  Step 2/4  ·  Next ▸  ·  ✕" control for walkthroughs. Unlike the overlay it is
/// clickable, but it never takes keyboard focus away from the app the user is working in.
/// </summary>
internal sealed class StepPillWindow : Window
{
    private readonly TextBlock _progress = new() { Foreground = Brushes.White, FontSize = 14, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 10, 0) };
    private readonly Button _back, _next, _close;
    private MonitorHandle? _monitor;

    public event Action? NextClicked;
    public event Action? BackClicked;
    public event Action? CloseClicked;

    public StepPillWindow()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        Title = "Rahbar steps";

        _back = PillButton("‹", "Previous step", () => BackClicked?.Invoke());
        _next = PillButton("Next ▸", "Next step (or say \"next\" / \"agla\")", () => NextClicked?.Invoke());
        _next.FontWeight = FontWeights.SemiBold;
        _close = PillButton("✕", "Stop (Esc)", () => CloseClicked?.Invoke());

        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(_back);
        row.Children.Add(_progress);
        row.Children.Add(Dot());
        row.Children.Add(_next);
        row.Children.Add(Dot());
        row.Children.Add(_close);

        Content = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0xF0, 0x0F, 0x76, 0x6E)),
            CornerRadius = new CornerRadius(20),
            Padding = new Thickness(8, 4, 8, 4),
            Margin = new Thickness(10), // room for the shadow
            Child = row,
            Effect = new DropShadowEffect { BlurRadius = 14, ShadowDepth = 2, Opacity = 0.45 },
        };

        SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            var ex = Win32.GetWindowLong(hwnd, Win32.GWL_EXSTYLE);
            Win32.SetWindowLong(hwnd, Win32.GWL_EXSTYLE, ex | Win32.WS_EX_TOOLWINDOW | Win32.WS_EX_NOACTIVATE);
            Win32.SetWindowDisplayAffinity(hwnd, Win32.WDA_EXCLUDEFROMCAPTURE);
        };
        SizeChanged += (_, _) => Place();
        DpiChanged += (_, _) => Dispatcher.BeginInvoke(Place);
    }

    public void ShowStep(MonitorHandle monitor, int index, int count, bool busy)
    {
        _monitor = monitor;
        _progress.Text = busy ? $"Step {index + 1}/{count} · looking…" : $"Step {index + 1}/{count}";
        _back.Visibility = index > 0 ? Visibility.Visible : Visibility.Collapsed;
        _next.Content = index == count - 1 ? "Done ✓" : "Next ▸";
        _next.IsEnabled = !busy;
        if (!IsVisible) Show();
        Place();
    }

    private void Place()
    {
        if (_monitor is null) return;
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;
        var dpi = VisualTreeHelper.GetDpi(this).DpiScaleX;
        var w = (int)Math.Ceiling(ActualWidth * dpi);
        var h = (int)Math.Ceiling(ActualHeight * dpi);
        var b = _monitor.Info.Bounds;
        var x = b.Left + (b.Width - w) / 2;
        var y = b.Bottom - h - (int)(14 * _monitor.Info.DpiScale);
        Win32.SetWindowPos(hwnd, Win32.HWND_TOPMOST, x, y, w, h, Win32.SWP_NOACTIVATE | Win32.SWP_SHOWWINDOW);
    }

    private static TextBlock Dot() => new() { Text = "·", Foreground = Brushes.White, Opacity = 0.6, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 0, 2, 0) };

    private static Button PillButton(string text, string tip, Action onClick)
    {
        var b = new Button
        {
            Content = text,
            ToolTip = tip,
            Foreground = Brushes.White,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(10, 4, 10, 4),
            Margin = new Thickness(0),
            MinWidth = 0,
            FontSize = 14,
            Cursor = Cursors.Hand,
            Focusable = false,
        };
        b.Click += (_, _) => onClick();
        return b;
    }
}
