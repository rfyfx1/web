using Rahbar.Core.Abstractions;

namespace Rahbar.Core.Pipeline;

/// <summary>Short memory: the last few questions and answers of this session.</summary>
public sealed class ConversationMemory
{
    private readonly object _gate = new();
    private readonly LinkedList<ConversationTurn> _turns = new();
    public int Capacity { get; }

    public ConversationMemory(int capacity = 3) => Capacity = capacity;

    public void Add(string question, string answer)
    {
        lock (_gate)
        {
            _turns.AddLast(new ConversationTurn(Trim(question), Trim(answer)));
            while (_turns.Count > Capacity) _turns.RemoveFirst();
        }
    }

    public IReadOnlyList<ConversationTurn> Recent()
    {
        lock (_gate) return _turns.ToList();
    }

    public void Clear()
    {
        lock (_gate) _turns.Clear();
    }

    private static string Trim(string s) => s.Length > 400 ? s[..400] + "…" : s;
}
