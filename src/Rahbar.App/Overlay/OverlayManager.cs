using Rahbar.App.Native;

namespace Rahbar.App.Overlay;

/// <summary>One overlay window per monitor, created when first needed.</summary>
internal sealed class OverlayManager
{
    private readonly Dictionary<string, OverlayWindow> _windows = new();

    public OverlayWindow For(MonitorHandle monitor)
    {
        var key = string.IsNullOrEmpty(monitor.Info.DeviceName) ? monitor.Handle.ToString() : monitor.Info.DeviceName;
        if (!_windows.TryGetValue(key, out var w))
        {
            w = new OverlayWindow(monitor);
            _windows[key] = w;
        }
        else
        {
            w.MoveTo(monitor); // resolution or DPI may have changed
        }
        w.EnsureShown();
        return w;
    }

    public void ClearAll()
    {
        foreach (var w in _windows.Values) w.FadeOutAll();
    }

    public void CloseAll()
    {
        foreach (var w in _windows.Values) w.Close();
        _windows.Clear();
    }
}
