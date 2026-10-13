using Rahbar.Core.Abstractions;

namespace Rahbar.Core.Geometry;

/// <summary>Was a mouse click on the thing Rahbar pointed at? (Used to auto-advance walkthroughs.)</summary>
public static class TargetHitTest
{
    /// <param name="radiusDip">How far from a circle/arrow target still counts, in DIPs (scaled by the monitor's DPI).</param>
    public static bool IsHit(PointTarget target, CoordinateMapper mapper, double screenX, double screenY, double radiusDip = 36)
    {
        if (!mapper.Monitor.Bounds.Contains(screenX, screenY)) return false;
        var centre = mapper.ImageToPhysical(target.X, target.Y);

        if (target.Type == PointType.Box && target.W > 0 && target.H > 0)
        {
            var halfW = target.W / mapper.Image.ScaleX / 2 + 6 * mapper.Monitor.DpiScale;
            var halfH = target.H / mapper.Image.ScaleY / 2 + 6 * mapper.Monitor.DpiScale;
            return Math.Abs(screenX - centre.X) <= halfW && Math.Abs(screenY - centre.Y) <= halfH;
        }

        var r = radiusDip * mapper.Monitor.DpiScale;
        var dx = screenX - centre.X;
        var dy = screenY - centre.Y;
        return dx * dx + dy * dy <= r * r;
    }
}
