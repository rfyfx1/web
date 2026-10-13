using System.Text.Json.Serialization;

namespace Rahbar.Core.Abstractions;

public sealed record ConversationTurn(string Question, string Answer);

public sealed class BrainRequest
{
    /// <summary>Encoded screenshot (JPEG or PNG) already downscaled.</summary>
    public required byte[] Image { get; init; }
    public string ImageMimeType { get; init; } = "image/jpeg";
    public required int ImageWidth { get; init; }
    public required int ImageHeight { get; init; }

    /// <summary>Full system prompt (already filled in by <see cref="Prompts.PromptBuilder"/>).</summary>
    public required string SystemPrompt { get; init; }

    /// <summary>The user-turn text (question plus short context).</summary>
    public required string UserText { get; init; }

    /// <summary>Set on the retry after a JSON parse failure.</summary>
    public bool StrictJsonReminder { get; init; }
}

/// <summary>Raw text returned by a brain; parsed by <see cref="Parsing.BrainReplyParser"/>.</summary>
public sealed record BrainReply(string RawText);

public enum PointType { Circle, Arrow, Box }

public sealed class PointTarget
{
    public double X { get; set; }
    public double Y { get; set; }
    public PointType Type { get; set; } = PointType.Circle;
    public double W { get; set; }
    public double H { get; set; }
    public string? Label { get; set; }
}

public sealed class BrainStep
{
    /// <summary>Text shown in the caption, in the user's own language and script.</summary>
    public string Say { get; set; } = "";

    /// <summary>
    /// Optional: the same sentence written in the native script (Urdu/Devanagari/…) so a native
    /// voice can pronounce it. Used when <see cref="Say"/> is Roman Urdu / Hinglish.
    /// </summary>
    public string? Speak { get; set; }

    /// <summary>ISO-639-1 code of <see cref="Speak"/> (e.g. "ur", "hi").</summary>
    public string? SpeakLanguage { get; set; }

    public PointTarget? Point { get; set; }
}

public sealed class BrainAnswer
{
    public List<BrainStep> Steps { get; set; } = new();

    /// <summary>ISO-639-1 code of the reply language ("en", "ur", "hi", "pa", "ar" …).</summary>
    public string Language { get; set; } = "en";

    public bool NeedsMoreSteps { get; set; }

    [JsonIgnore]
    public string FullText => string.Join(" ", Steps.Select(s => s.Say));
}

/// <summary>Something to say aloud: the text plus a language/script hint for voice choice.</summary>
public sealed record SpeechLine(string Text, string? LanguageCode);
