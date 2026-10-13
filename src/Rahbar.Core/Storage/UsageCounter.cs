using System.Text.Json;

namespace Rahbar.Core.Storage;

/// <summary>Counts today's requests per provider so free-tier limits are visible.</summary>
public sealed class UsageCounter
{
    private sealed class Data
    {
        public string Date { get; set; } = "";
        public Dictionary<string, int> Counts { get; set; } = new();
    }

    private readonly object _gate = new();
    private readonly string _path;
    private readonly Func<DateTime> _now;
    private Data _data;

    public UsageCounter(string path, Func<DateTime>? now = null)
    {
        _path = path;
        _now = now ?? (() => DateTime.Now);
        _data = Load(path);
    }

    private string Today => _now().ToString("yyyy-MM-dd");

    public void Increment(string provider)
    {
        lock (_gate)
        {
            Roll();
            _data.Counts[provider] = _data.Counts.GetValueOrDefault(provider) + 1;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                File.WriteAllText(_path, JsonSerializer.Serialize(_data));
            }
            catch (IOException) { /* counting is best-effort */ }
        }
    }

    public IReadOnlyDictionary<string, int> TodayCounts()
    {
        lock (_gate)
        {
            Roll();
            return new Dictionary<string, int>(_data.Counts);
        }
    }

    public string Summary()
    {
        var c = TodayCounts();
        return c.Count == 0 ? "Today: no requests yet" : "Today: " + string.Join(" · ", c.OrderBy(k => k.Key).Select(k => $"{k.Key} {k.Value}"));
    }

    private void Roll()
    {
        if (_data.Date != Today) _data = new Data { Date = Today };
    }

    private static Data Load(string path)
    {
        try { return File.Exists(path) ? JsonSerializer.Deserialize<Data>(File.ReadAllText(path)) ?? new() : new(); }
        catch (JsonException) { return new(); }
    }
}
