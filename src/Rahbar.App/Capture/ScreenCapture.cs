using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Rahbar.App.Native;
using Rahbar.Core.Geometry;

namespace Rahbar.App.Capture;

/// <summary>A screenshot of one monitor, already downscaled for the AI.</summary>
internal sealed record ScreenShot(byte[] Jpeg, CoordinateMapper Mapper, MonitorHandle Monitor);

/// <summary>
/// Captures only the monitor under the cursor, only when asked (never continuously).
/// Uses GDI BitBlt, which is fast and DPI-correct for a Per-Monitor-V2 process.
/// </summary>
internal static class ScreenCapture
{
    /// <summary>Grabs raw pixels. Call on the UI thread right when the hotkey goes down.</summary>
    public static BitmapSource GrabRaw(MonitorHandle monitor)
    {
        var b = monitor.Info.Bounds;
        var screenDc = Win32.GetDC(IntPtr.Zero);
        var memDc = Win32.CreateCompatibleDC(screenDc);
        var bmp = Win32.CreateCompatibleBitmap(screenDc, b.Width, b.Height);
        var old = Win32.SelectObject(memDc, bmp);
        try
        {
            Win32.BitBlt(memDc, 0, 0, b.Width, b.Height, screenDc, b.Left, b.Top, Win32.SRCCOPY | Win32.CAPTUREBLT);
            Win32.SelectObject(memDc, old);
            var src = Imaging.CreateBitmapSourceFromHBitmap(bmp, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            src.Freeze();
            return src;
        }
        finally
        {
            Win32.DeleteObject(bmp);
            Win32.DeleteDC(memDc);
            Win32.ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

    /// <summary>Downscales and JPEG-encodes. Safe to run on a background thread (input is frozen).</summary>
    public static ScreenShot Encode(BitmapSource raw, MonitorHandle monitor, int maxLongEdge)
    {
        var mapper = CoordinateMapper.ForCapture(monitor.Info, maxLongEdge);
        BitmapSource img = raw;
        if (mapper.Image.ScaleX < 1.0 || mapper.Image.ScaleY < 1.0)
        {
            var scaled = new TransformedBitmap(raw, new ScaleTransform(
                (double)mapper.Image.Width / raw.PixelWidth, (double)mapper.Image.Height / raw.PixelHeight));
            scaled.Freeze();
            img = scaled;
        }

        var enc = new JpegBitmapEncoder { QualityLevel = 85 };
        enc.Frames.Add(BitmapFrame.Create(img));
        using var ms = new MemoryStream();
        enc.Save(ms);
        return new ScreenShot(ms.ToArray(), mapper, monitor);
    }
}
