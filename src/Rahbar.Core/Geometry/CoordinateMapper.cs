namespace Rahbar.Core.Geometry;

public readonly record struct PixelRect(int Left, int Top, int Width, int Height)
{
    public int Right => Left + Width;
    public int Bottom => Top + Height;
    public bool Contains(double x, double y) => x >= Left && x < Right && y >= Top && y < Bottom;
}

public readonly record struct ScreenPoint(double X, double Y);

/// <summary>
/// A monitor in Windows' virtual desktop. With Per-Monitor-V2 DPI awareness, bounds are in real
/// (physical) pixels and can be negative for monitors left of / above the primary one.
/// </summary>
/// <param name="DpiScale">1.0 = 100 %, 1.25 = 125 %, 1.5 = 150 %, 2.0 = 200 %.</param>
public sealed record MonitorInfo(PixelRect Bounds, double DpiScale, string DeviceName = "")
{
    public static double ScaleFromDpi(uint dpi) => dpi <= 0 ? 1.0 : dpi / 96.0;
}

/// <summary>Size of the screenshot after downscaling, plus the scale used.</summary>
public readonly record struct ImageSize(int Width, int Height, double ScaleX, double ScaleY);

/// <summary>
/// Maps between three spaces:
///  1. image pixels — the downscaled screenshot the model saw (and answers in);
///  2. physical screen pixels — absolute virtual-desktop coordinates (mouse hooks, SendInput);
///  3. overlay DIPs — WPF units inside the transparent overlay window that covers one monitor.
/// </summary>
public sealed class CoordinateMapper
{
    public const int DefaultMaxLongEdge = 1568;

    public MonitorInfo Monitor { get; }
    public ImageSize Image { get; }

    public CoordinateMapper(MonitorInfo monitor, ImageSize image)
    {
        if (monitor.Bounds.Width <= 0 || monitor.Bounds.Height <= 0) throw new ArgumentException("Monitor has no area.", nameof(monitor));
        if (image.Width <= 0 || image.Height <= 0) throw new ArgumentException("Image has no area.", nameof(image));
        Monitor = monitor;
        Image = image;
    }

    public static CoordinateMapper ForCapture(MonitorInfo monitor, int maxLongEdge = DefaultMaxLongEdge) =>
        new(monitor, ComputeImageSize(monitor.Bounds.Width, monitor.Bounds.Height, maxLongEdge));

    /// <summary>Shrinks so the long edge is at most <paramref name="maxLongEdge"/>; never enlarges.</summary>
    public static ImageSize ComputeImageSize(int physicalWidth, int physicalHeight, int maxLongEdge = DefaultMaxLongEdge)
    {
        if (physicalWidth <= 0 || physicalHeight <= 0) throw new ArgumentOutOfRangeException(nameof(physicalWidth));
        var longEdge = Math.Max(physicalWidth, physicalHeight);
        var s = longEdge > maxLongEdge ? (double)maxLongEdge / longEdge : 1.0;
        var w = Math.Max(1, (int)Math.Round(physicalWidth * s));
        var h = Math.Max(1, (int)Math.Round(physicalHeight * s));
        // Use the exact per-axis ratios after rounding so mapping back is precise.
        return new ImageSize(w, h, (double)w / physicalWidth, (double)h / physicalHeight);
    }

    /// <summary>Image pixel → absolute physical screen pixel.</summary>
    public ScreenPoint ImageToPhysical(double x, double y) =>
        new(Monitor.Bounds.Left + x / Image.ScaleX, Monitor.Bounds.Top + y / Image.ScaleY);

    /// <summary>Image pixel → DIP position inside this monitor's overlay window.</summary>
    public ScreenPoint ImageToOverlay(double x, double y) =>
        new(x / Image.ScaleX / Monitor.DpiScale, y / Image.ScaleY / Monitor.DpiScale);

    /// <summary>Image-space length (e.g. a box width) → overlay DIPs.</summary>
    public double ImageLengthToOverlay(double length, bool horizontal = true) =>
        length / (horizontal ? Image.ScaleX : Image.ScaleY) / Monitor.DpiScale;

    /// <summary>Absolute physical screen pixel → image pixel (e.g. where the cursor is in the screenshot).</summary>
    public ScreenPoint PhysicalToImage(double screenX, double screenY) =>
        new((screenX - Monitor.Bounds.Left) * Image.ScaleX, (screenY - Monitor.Bounds.Top) * Image.ScaleY);
}
