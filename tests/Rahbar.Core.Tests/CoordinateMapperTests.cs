using Rahbar.Core.Geometry;

namespace Rahbar.Core.Tests;

public class CoordinateMapperTests
{
    private const double Tol = 0.51; // rounding to whole pixels when downscaling

    [Theory]
    [InlineData(1920, 1080, 1.00)]
    [InlineData(1920, 1080, 1.25)]
    [InlineData(2880, 1620, 1.50)]
    [InlineData(3840, 2160, 1.50)]
    [InlineData(3840, 2160, 2.00)]
    public void Primary_monitor_round_trips_at_common_scales(int w, int h, double dpi)
    {
        var m = CoordinateMapper.ForCapture(new MonitorInfo(new PixelRect(0, 0, w, h), dpi));

        // A real button at physical (1500, 900) should come back to the same place.
        var img = m.PhysicalToImage(1500, 900);
        var phys = m.ImageToPhysical(img.X, img.Y);
        Assert.InRange(phys.X, 1500 - Tol, 1500 + Tol);
        Assert.InRange(phys.Y, 900 - Tol, 900 + Tol);

        // Overlay DIPs = physical offset within the monitor / DPI scale.
        var ov = m.ImageToOverlay(img.X, img.Y);
        Assert.InRange(ov.X, 1500 / dpi - Tol, 1500 / dpi + Tol);
        Assert.InRange(ov.Y, 900 / dpi - Tol, 900 / dpi + Tol);
    }

    [Fact]
    public void Downscales_4k_to_1568_long_edge_and_keeps_aspect()
    {
        var size = CoordinateMapper.ComputeImageSize(3840, 2160);
        Assert.Equal(1568, size.Width);
        Assert.Equal(882, size.Height);
        Assert.Equal(1568.0 / 3840, size.ScaleX, 6);
    }

    [Fact]
    public void Does_not_upscale_small_screens()
    {
        var size = CoordinateMapper.ComputeImageSize(1366, 768);
        Assert.Equal((1366, 768, 1.0, 1.0), (size.Width, size.Height, size.ScaleX, size.ScaleY));
    }

    [Fact]
    public void Portrait_monitor_uses_height_as_long_edge()
    {
        var size = CoordinateMapper.ComputeImageSize(2160, 3840);
        Assert.Equal(1568, size.Height);
        Assert.Equal(882, size.Width);
    }

    [Theory]
    [InlineData(1.00)]
    [InlineData(1.25)]
    [InlineData(1.50)]
    [InlineData(2.00)]
    public void Second_monitor_left_of_primary_has_negative_coordinates(double dpi)
    {
        // 2560x1440 monitor placed to the left of the primary: x from -2560 to 0.
        var mon = new MonitorInfo(new PixelRect(-2560, 0, 2560, 1440), dpi);
        var m = CoordinateMapper.ForCapture(mon);

        var centre = m.ImageToPhysical(m.Image.Width / 2.0, m.Image.Height / 2.0);
        Assert.InRange(centre.X, -1280 - 1, -1280 + 1);
        Assert.InRange(centre.Y, 720 - 1, 720 + 1);

        var topLeft = m.ImageToPhysical(0, 0);
        Assert.Equal(-2560, topLeft.X);
        Assert.Equal(0, topLeft.Y);

        // Overlay coordinates are local to the monitor, so never negative.
        var ov = m.ImageToOverlay(m.Image.Width / 2.0, m.Image.Height / 2.0);
        Assert.InRange(ov.X, 1280 / dpi - 1, 1280 / dpi + 1);
        Assert.True(ov.X >= 0 && ov.Y >= 0);
    }

    [Theory]
    [InlineData(1.00)]
    [InlineData(1.25)]
    [InlineData(1.50)]
    [InlineData(2.00)]
    public void Second_monitor_above_primary_has_negative_y(double dpi)
    {
        var mon = new MonitorInfo(new PixelRect(-300, -864, 1536, 864), dpi);
        var m = CoordinateMapper.ForCapture(mon);
        Assert.Equal(1.0, m.Image.ScaleX); // under 1568 px: not downscaled

        var p = m.ImageToPhysical(768, 432);
        Assert.Equal(468, p.X);
        Assert.Equal(-432, p.Y);

        var back = m.PhysicalToImage(468, -432);
        Assert.Equal(768, back.X, 6);
        Assert.Equal(432, back.Y, 6);

        var ov = m.ImageToOverlay(768, 432);
        Assert.Equal(768 / dpi, ov.X, 6);
        Assert.Equal(432 / dpi, ov.Y, 6);
    }

    [Fact]
    public void Downscaled_4k_at_150_percent_maps_image_point_to_correct_overlay_dip()
    {
        // 4K monitor at 150 % sits right of a 1080p primary.
        var mon = new MonitorInfo(new PixelRect(1920, 0, 3840, 2160), 1.5);
        var m = CoordinateMapper.ForCapture(mon);

        // The model points at image (784, 441) = middle of the downscaled 1568x882 image.
        var phys = m.ImageToPhysical(784, 441);
        Assert.Equal(1920 + 1920, phys.X, 3);
        Assert.Equal(1080, phys.Y, 3);

        var ov = m.ImageToOverlay(784, 441);
        Assert.Equal(1280, ov.X, 3); // 1920 physical px / 1.5
        Assert.Equal(720, ov.Y, 3);

        Assert.Equal(40 / (1568.0 / 3840) / 1.5, m.ImageLengthToOverlay(40), 6);
    }

    [Fact]
    public void Scale_from_dpi()
    {
        Assert.Equal(1.0, MonitorInfo.ScaleFromDpi(96));
        Assert.Equal(1.25, MonitorInfo.ScaleFromDpi(120));
        Assert.Equal(1.5, MonitorInfo.ScaleFromDpi(144));
        Assert.Equal(2.0, MonitorInfo.ScaleFromDpi(192));
    }
}
