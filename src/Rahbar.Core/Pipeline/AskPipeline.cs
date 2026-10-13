using Rahbar.Core.Abstractions;
using Rahbar.Core.Language;
using Rahbar.Core.Parsing;
using Rahbar.Core.Prompts;

namespace Rahbar.Core.Pipeline;

/// <summary>What the app captured when the hotkey went down.</summary>
public sealed class CapturedScreen
{
    public required byte[] Image { get; init; }
    public string MimeType { get; init; } = "image/jpeg";
    public required ScreenContext Context { get; init; }
}

public sealed record AskResult(string Question, BrainAnswer Answer, bool WasExplainScreen);

/// <summary>
/// The core flow, free of any Windows code so it can be tested:
/// speech → text → (screenshot + question) → brain → parsed steps.
/// </summary>
public sealed class AskPipeline
{
    private readonly ISpeechToText _stt;
    private readonly IVisionBrain _brain;
    private readonly ConversationMemory _memory;

    public AskPipeline(ISpeechToText stt, IVisionBrain brain, ConversationMemory memory)
    {
        _stt = stt;
        _brain = brain;
        _memory = memory;
    }

    /// <summary>Raised with the provider name for every paid/free-tier request (usage counter).</summary>
    public event Action<string>? RequestMade;

    public string BrainName => _brain.Name;

    public async Task<string> TranscribeAsync(byte[]? wav, bool hasSpeech, LanguageProfile lang, CancellationToken ct)
    {
        if (wav is null || !hasSpeech) return "";
        var text = await _stt.TranscribeAsync(wav, lang.WhisperLanguage, lang.WhisperStylePrompt, ct).ConfigureAwait(false);
        RequestMade?.Invoke((_stt as FallbackSpeechToText)?.LastUsed?.Name ?? _stt.Name);
        text = text.Trim();
        return TranscriptFilter.IsLikelyHallucination(text, lang.WhisperStylePrompt) ? "" : text;
    }

    public async Task<AskResult> AskAsync(CapturedScreen screen, string question, LanguageProfile lang,
        string? extraInstructions, CancellationToken ct)
    {
        var explain = string.IsNullOrWhiteSpace(question);
        var system = PromptBuilder.BuildSystemPrompt(screen.Context, lang, extraInstructions);
        var userText = PromptBuilder.BuildUserText(question, screen.Context, _memory.Recent());

        var answer = await AskParsedAsync(screen, system, userText, ct).ConfigureAwait(false);
        _memory.Add(explain ? "(explain this screen)" : question, answer.FullText);
        return new AskResult(explain ? "" : question, answer, explain);
    }

    /// <summary>
    /// Walkthrough step 2+: asks again with a fresh screenshot for just the next step.
    /// <paramref name="stepIndex"/> is the zero-based index of the step to produce.
    /// </summary>
    public async Task<BrainStep> ContinueAsync(CapturedScreen screen, string originalQuestion, IReadOnlyList<string> plannedSteps,
        int stepIndex, LanguageProfile lang, string? extraInstructions, CancellationToken ct)
    {
        var system = PromptBuilder.BuildSystemPrompt(screen.Context, lang, extraInstructions);
        var userText = PromptBuilder.BuildContinueText(originalQuestion, plannedSteps, stepIndex, screen.Context);
        var answer = await AskParsedAsync(screen, system, userText, ct).ConfigureAwait(false);
        return answer.Steps[0];
    }

    private async Task<BrainAnswer> AskParsedAsync(CapturedScreen screen, string system, string userText, CancellationToken ct)
    {
        BrainRequest Request(bool strict) => new()
        {
            Image = screen.Image,
            ImageMimeType = screen.MimeType,
            ImageWidth = screen.Context.ImageWidth,
            ImageHeight = screen.Context.ImageHeight,
            SystemPrompt = system,
            UserText = strict ? userText + "\n\nReturn only valid JSON." : userText,
            StrictJsonReminder = strict,
        };

        var reply = await _brain.AskAsync(Request(false), ct).ConfigureAwait(false);
        RequestMade?.Invoke(_brain.Name);
        if (BrainReplyParser.TryParse(reply.RawText, screen.Context.ImageWidth, screen.Context.ImageHeight, out var answer, out _))
            return answer;

        reply = await _brain.AskAsync(Request(true), ct).ConfigureAwait(false);
        RequestMade?.Invoke(_brain.Name);
        if (BrainReplyParser.TryParse(reply.RawText, screen.Context.ImageWidth, screen.Context.ImageHeight, out answer, out _))
            return answer;

        // Last resort: speak whatever plain text came back rather than failing silently.
        var plain = reply.RawText?.Trim();
        if (string.IsNullOrEmpty(plain) || plain.StartsWith('{'))
            throw new FriendlyException("The AI sent an answer I couldn't read. Please ask again.");
        return new BrainAnswer { Steps = { new BrainStep { Say = plain.Length > 600 ? plain[..600] : plain } } };
    }
}

public static class TranscriptFilter
{
    // Whisper sometimes "hears" these on near-silence.
    private static readonly string[] Phantoms =
    {
        "thank you", "thank you.", "thanks for watching", "thanks for watching!", "you", ".", "bye",
        "شکریہ", "धन्यवाद", "subtitles by the amara.org community",
    };

    public static bool IsLikelyHallucination(string text, string? stylePrompt)
    {
        if (string.IsNullOrWhiteSpace(text)) return true;
        var t = text.Trim().ToLowerInvariant();
        if (Phantoms.Contains(t)) return true;
        // Whisper occasionally echoes the style prompt back on silence.
        return stylePrompt is not null && t == stylePrompt.Trim().ToLowerInvariant();
    }
}
