using System.Text.Json;

namespace Rahbar.Core.Storage;

public sealed record HistoryEntry(DateTime Time, string Question, string Answer, string Language, string App);

/// <summary>Last 50 questions and answers, text only (never screenshots).</summary>
public sealed class HistoryStore
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private readonly object _gate = new();
    private readonly string _path;
    private List<HistoryEntry> _items;

    public int Capacity { get; }

    public HistoryStore(string path, int capacity = 50)
    {
        _path = path;
        Capacity = capacity;
        _items = Load(path);
    }

    public event Action? Changed;

    public void Add(HistoryEntry e)
    {
        lock (_gate)
        {
            _items.Insert(0, e);
            if (_items.Count > Capacity) _items.RemoveRange(Capacity, _items.Count - Capacity);
            Save();
        }
        Changed?.Invoke();
    }

    /// <summary>Newest first; filters by words in the question, answer or app name.</summary>
    public IReadOnlyList<HistoryEntry> Search(string? query)
    {
        lock (_gate)
        {
            if (string.IsNullOrWhiteSpace(query)) return _items.ToList();
            var terms = query.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return _items.Where(e => terms.All(t =>
                e.Question.Contains(t, StringComparison.OrdinalIgnoreCase) ||
                e.Answer.Contains(t, StringComparison.OrdinalIgnoreCase) ||
                e.App.Contains(t, StringComparison.OrdinalIgnoreCase))).ToList();
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _items.Clear();
            Save();
        }
        Changed?.Invoke();
    }

    private void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var tmp = _path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(_items, Json));
        File.Move(tmp, _path, overwrite: true);
    }

    private static List<HistoryEntry> Load(string path)
    {
        try
        {
            return File.Exists(path) ? JsonSerializer.Deserialize<List<HistoryEntry>>(File.ReadAllText(path)) ?? new() : new();
        }
        catch (JsonException)
        {
            return new();
        }
    }
}
