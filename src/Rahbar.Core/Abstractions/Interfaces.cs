using Rahbar.Core.Language;

namespace Rahbar.Core.Abstractions;

/// <summary>Looks at a screenshot plus a question and returns what to say and where to point.</summary>
public interface IVisionBrain
{
    string Name { get; }
    Task<BrainReply> AskAsync(BrainRequest req, CancellationToken ct);
}

/// <summary>Turns recorded speech (16 kHz mono WAV) into text.</summary>
public interface ISpeechToText
{
    string Name { get; }

    /// <param name="languageHint">ISO-639-1 code ("en", "ur", "hi") or null to auto-detect.</param>
    /// <param name="stylePrompt">Optional sample text that nudges the transcript's script/style (e.g. Roman Urdu).</param>
    Task<string> TranscribeAsync(byte[] wav, string? languageHint, string? stylePrompt, CancellationToken ct);
}

/// <summary>Speaks text aloud.</summary>
public interface ITextToSpeech
{
    string Name { get; }
    IReadOnlyList<VoiceInfo> Voices { get; }

    /// <returns>False when no installed voice can read this text (the caption still shows it).</returns>
    Task<bool> SpeakAsync(SpeechLine line, CancellationToken ct);
    void Stop();
}

/// <summary>Stores API keys outside the settings file (Windows Credential Manager in the app).</summary>
public interface ISecretStore
{
    string? Get(string name);
    void Set(string name, string? value);
}

/// <summary>A provider that can check its key/connection from the Settings "Test" button.</summary>
public interface ITestableProvider
{
    /// <returns>A short OK message; throws <see cref="FriendlyException"/> on failure.</returns>
    Task<string> TestAsync(CancellationToken ct);
}
