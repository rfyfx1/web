using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using Rahbar.App.Native;
using Rahbar.Core.Abstractions;
using Rahbar.Core.Language;

namespace Rahbar.App.Overlay;

public enum GlowState { None, Listening, Thinking }

/// <summary>
/// A transparent, topmost, click-through window covering one monitor. Everything Rahbar draws
/// on screen lives here; mouse clicks pass straight through to the apps underneath.
/// </summary>
internal sealed class OverlayWindow : Window
{
    private static readonly Color Accent = Color.FromRgb(0x14, 0xB8, 0xA6);    // teal
    private static readonly Color Listening = Color.FromRgb(0xEF, 0x44, 0x44); // red = mic on
    private static readonly Color Thinking = Color.FromRgb(0xF5, 0x9E, 0x0B);  // amber

    private readonly Canvas _pointerLayer = new();
    private readonly Border _glow;
    private readonly Border _caption;
    private readonly TextBlock _captionText;
    private MonitorHandle _monitor;

    public OverlayWindow(MonitorHandle monitor)
    {
        _monitor = monitor;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        ResizeMode = ResizeMode.NoResize;
        Focusable = false;
        IsHitTestVisible = false;
        Title = "Rahbar overlay";

        _glow = new Border
        {
            BorderThickness = new Thickness(5),
            Opacity = 0,
            IsHitTestVisible = false,
        };

        _captionText = new TextBlock
        {
            Foreground = Brushes.White,
            FontSize = 20,
            FontFamily = new FontFamily("Segoe UI, Nirmala UI, Urdu Typesetting, Segoe UI Historic"),
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
            LineHeight = 30,
        };
        _caption = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0xE6, 0x11, 0x18, 0x27)),
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(20, 12, 20, 12),
            Child = _captionText,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 0, 70),
            Opacity = 0,
            Effect = new DropShadowEffect { BlurRadius = 18, ShadowDepth = 2, Opacity = 0.5 },
        };

        var root = new Grid();
        root.Children.Add(_pointerLayer);
        root.Children.Add(_glow);
        root.Children.Add(_caption);
        Content = root;

        SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            var ex = Win32.GetWindowLong(hwnd, Win32.GWL_EXSTYLE);
            Win32.SetWindowLong(hwnd, Win32.GWL_EXSTYLE,
                ex | Win32.WS_EX_TRANSPARENT | Win32.WS_EX_LAYERED | Win32.WS_EX_TOOLWINDOW | Win32.WS_EX_NOACTIVATE);
            PlaceOnMonitor();
        };
        // WPF resizes the window when it lands on a monitor with a different DPI; put it back.
        DpiChanged += (_, _) => Dispatcher.BeginInvoke(PlaceOnMonitor);
    }

    public MonitorHandle Monitor => _monitor;

    /// <summary>DPI scale WPF is actually using for this window.</summary>
    public double DpiScale => VisualTreeHelper.GetDpi(this).DpiScaleX;

    public void MoveTo(MonitorHandle monitor)
    {
        _monitor = monitor;
        if (IsLoaded) PlaceOnMonitor();
    }

    public void EnsureShown()
    {
        if (!IsVisible) Show();
        PlaceOnMonitor();
    }

    private void PlaceOnMonitor()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;
        var b = _monitor.Info.Bounds;
        Win32.SetWindowPos(hwnd, Win32.HWND_TOPMOST, b.Left, b.Top, b.Width, b.Height, Win32.SWP_NOACTIVATE | Win32.SWP_SHOWWINDOW);
        _caption.MaxWidth = Math.Max(300, b.Width / _monitor.Info.DpiScale * 0.7);
    }

    // ---------- Edge glow (visible whenever the mic is on or Rahbar is thinking) ----------

    public void SetGlow(GlowState state)
    {
        if (state == GlowState.None)
        {
            Fade(_glow, 0, 200);
            return;
        }

        var c = state == GlowState.Listening ? Listening : Thinking;
        _glow.BorderBrush = new SolidColorBrush(c);
        _glow.Effect = new DropShadowEffect { Color = c, BlurRadius = 40, ShadowDepth = 0, Opacity = 1 };
        var pulse = new DoubleAnimation(1.0, 0.45, TimeSpan.FromMilliseconds(state == GlowState.Listening ? 900 : 500))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase(),
        };
        _glow.BeginAnimation(OpacityProperty, pulse);
    }

    // ---------- Caption bubble ----------

    public void ShowCaption(string text)
    {
        _captionText.Text = text;
        var rtl = ScriptDetector.Detect(text) == TextScript.Arabic;
        _captionText.FlowDirection = rtl ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        _captionText.FontSize = rtl ? 24 : 20; // Urdu script reads better a little larger
        Fade(_caption, 1, 180);
    }

    public void HideCaption() => Fade(_caption, 0, 250);

    // ---------- Pointers ----------

    /// <param name="x">Target centre in overlay DIPs.</param>
    /// <param name="from">Arrow start (e.g. the cursor) in overlay DIPs, if on this monitor.</param>
    public void ShowPointer(PointType type, double x, double y, double w, double h, string? label, Point? from)
    {
        ClearPointer();
        switch (type)
        {
            case PointType.Box:
                DrawBox(x, y, w, h);
                break;
            case PointType.Arrow:
                DrawArrow(x, y, from);
                DrawCircle(x, y, small: true);
                break;
            default:
                DrawCircle(x, y, small: false);
                break;
        }
        if (!string.IsNullOrWhiteSpace(label)) DrawLabel(label!, x, y, type == PointType.Box ? Math.Max(w, 48) / 2 : 30);
        Fade(_pointerLayer, 1, 200);
    }

    public void ClearPointer()
    {
        foreach (UIElement child in _pointerLayer.Children) child.BeginAnimation(OpacityProperty, null);
        _pointerLayer.Children.Clear();
    }

    public void FadeOutAll()
    {
        Fade(_pointerLayer, 0, 300, ClearPointer);
        HideCaption();
        SetGlow(GlowState.None);
    }

    private void DrawCircle(double x, double y, bool small)
    {
        double d = small ? 34 : 48;
        var brush = new SolidColorBrush(Accent);

        var ring = new Ellipse { Width = d, Height = d, Stroke = brush, StrokeThickness = 4 };
        ring.Effect = new DropShadowEffect { Color = Accent, BlurRadius = 16, ShadowDepth = 0, Opacity = 0.9 };
        Place(ring, x - d / 2, y - d / 2);

        // Expanding, fading ring: the "pulse".
        var pulse = new Ellipse
        {
            Width = d, Height = d, Stroke = brush, StrokeThickness = 3,
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = new ScaleTransform(1, 1),
        };
        Place(pulse, x - d / 2, y - d / 2);
        var period = TimeSpan.FromSeconds(1.2);
        var grow = new DoubleAnimation(1, 1.8, period) { RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new QuadraticEase() };
        var fade = new DoubleAnimation(0.9, 0, period) { RepeatBehavior = RepeatBehavior.Forever };
        pulse.RenderTransform.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
        pulse.RenderTransform.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
        pulse.BeginAnimation(OpacityProperty, fade);

        var dot = new Ellipse { Width = 8, Height = 8, Fill = brush };
        Place(dot, x - 4, y - 4);
    }

    private void DrawBox(double x, double y, double w, double h)
    {
        if (w < 8) w = 120;
        if (h < 8) h = 48;
        var pad = 6.0;
        var rect = new Rect(x - w / 2 - pad, y - h / 2 - pad, w + 2 * pad, h + 2 * pad);

        // Spotlight: dim everything except the target.
        var full = new RectangleGeometry(new Rect(0, 0, ActualWidth, ActualHeight));
        var hole = new RectangleGeometry(rect, 10, 10);
        var dim = new Path
        {
            Data = new CombinedGeometry(GeometryCombineMode.Exclude, full, hole),
            Fill = new SolidColorBrush(Color.FromArgb(0x66, 0, 0, 0)),
        };
        _pointerLayer.Children.Add(dim);

        var border = new Rectangle
        {
            Width = rect.Width, Height = rect.Height, RadiusX = 10, RadiusY = 10,
            Stroke = new SolidColorBrush(Accent), StrokeThickness = 3,
            Effect = new DropShadowEffect { Color = Accent, BlurRadius = 18, ShadowDepth = 0 },
        };
        Place(border, rect.X, rect.Y);
    }

    private void DrawArrow(double tx, double ty, Point? from)
    {
        var start = from is { } f && (Math.Abs(f.X - tx) > 60 || Math.Abs(f.Y - ty) > 60)
            ? f
            : new Point(tx + (tx > ActualWidth / 2 ? -180 : 180), ty + (ty > ActualHeight / 2 ? -140 : 140));

        // Stop short of the target so the arrow tip sits on the circle edge.
        var v = new Vector(tx - start.X, ty - start.Y);
        var len = v.Length;
        v.Normalize();
        var end = new Point(tx - v.X * 22, ty - v.Y * 22);
        var normal = new Vector(-v.Y, v.X);
        var control = start + (end - start) / 2 + normal * Math.Min(80, len * 0.25);

        var brush = new SolidColorBrush(Accent);
        var curve = new PathGeometry(new[]
        {
            new PathFigure(start, new[] { new QuadraticBezierSegment(control, end, true) }, false),
        });
        _pointerLayer.Children.Add(new Path
        {
            Data = curve, Stroke = brush, StrokeThickness = 4,
            StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
            Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 6, ShadowDepth = 1, Opacity = 0.5 },
        });

        // Arrow head along the curve's final tangent.
        var dir = end - control;
        dir.Normalize();
        var side = new Vector(-dir.Y, dir.X);
        var head = new PathGeometry(new[]
        {
            new PathFigure(end, new PathSegment[]
            {
                new LineSegment(end - dir * 16 + side * 9, true),
                new LineSegment(end - dir * 16 - side * 9, true),
            }, true),
        });
        _pointerLayer.Children.Add(new Path { Data = head, Fill = brush });
    }

    private void DrawLabel(string text, double x, double y, double offset)
    {
        var tb = new TextBlock
        {
            Text = text, Foreground = Brushes.White, FontSize = 14, FontWeight = FontWeights.SemiBold,
            FontFamily = new FontFamily("Segoe UI, Nirmala UI, Urdu Typesetting"),
        };
        var bubble = new Border
        {
            Background = new SolidColorBrush(Accent),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(8, 3, 8, 4),
            Child = tb,
        };
        bubble.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var bw = bubble.DesiredSize.Width;
        var left = x + offset + 8;
        if (left + bw > ActualWidth - 8) left = x - offset - 8 - bw; // flip to the left near the edge
        Place(bubble, Math.Max(4, left), Math.Max(4, y - bubble.DesiredSize.Height / 2));
    }

    private void Place(UIElement e, double left, double top)
    {
        Canvas.SetLeft(e, left);
        Canvas.SetTop(e, top);
        _pointerLayer.Children.Add(e);
    }

    private static void Fade(UIElement e, double to, int ms, Action? done = null)
    {
        var a = new DoubleAnimation(to, TimeSpan.FromMilliseconds(ms)) { EasingFunction = new QuadraticEase() };
        if (done is not null) a.Completed += (_, _) => done();
        e.BeginAnimation(OpacityProperty, a);
    }
}
