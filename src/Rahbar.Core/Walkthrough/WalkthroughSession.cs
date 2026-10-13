using Rahbar.Core.Abstractions;

namespace Rahbar.Core.Walkthrough;

/// <summary>Where we are in a multi-step answer.</summary>
public sealed class WalkthroughSession
{
    private readonly List<BrainStep> _steps;

    public WalkthroughSession(string question, BrainAnswer answer)
    {
        if (answer.Steps.Count == 0) throw new ArgumentException("Answer has no steps.", nameof(answer));
        Question = question;
        Language = answer.Language;
        _steps = answer.Steps.ToList();
        Planned = _steps.Select(s => s.Say).ToList();
    }

    public string Question { get; }
    public string Language { get; }

    /// <summary>The original plan's text, used to keep the AI on track for later steps.</summary>
    public IReadOnlyList<string> Planned { get; }

    public int Index { get; private set; }
    public int Count => _steps.Count;
    public BrainStep Current => _steps[Index];
    public bool IsMultiStep => _steps.Count > 1;
    public bool IsLast => Index == _steps.Count - 1;

    /// <summary>Whether the current step came from a screenshot taken for that step (vs. the original plan).</summary>
    public bool CurrentIsFresh => Index == 0 || _fresh.Contains(Index);
    private readonly HashSet<int> _fresh = new();

    public bool MoveNext()
    {
        if (IsLast) return false;
        Index++;
        return true;
    }

    public bool MoveBack()
    {
        if (Index == 0) return false;
        Index--;
        return true;
    }

    /// <summary>Replaces the current step with one located on a fresh screenshot.</summary>
    public void ReplaceCurrent(BrainStep step)
    {
        _steps[Index] = step;
        _fresh.Add(Index);
    }

    public string Progress => $"{Index + 1}/{Count}";
}
