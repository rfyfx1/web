using Rahbar.Core.Abstractions;

namespace Rahbar.Core.Pipeline;

/// <summary>Tries each speech-to-text provider in order (e.g. Groq, then offline Windows recognition).</summary>
public sealed class FallbackSpeechToText : ISpeechToText
{
    private readonly IReadOnlyList<ISpeechToText> _providers;
    public event Action<ISpeechToText, Exception>? ProviderFailed;

    public FallbackSpeechToText(params ISpeechToText[] providers)
    {
        if (providers.Length == 0) throw new ArgumentException("At least one provider is required.", nameof(providers));
        _providers = providers;
    }

    public string Name => string.Join(" → ", _providers.Select(p => p.Name));

    /// <summary>The provider that produced the last transcript.</summary>
    public ISpeechToText? LastUsed { get; private set; }

    public async Task<string> TranscribeAsync(byte[] wav, string? languageHint, string? stylePrompt, CancellationToken ct)
    {
        Exception? last = null;
        foreach (var p in _providers)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var text = await p.TranscribeAsync(wav, languageHint, stylePrompt, ct).ConfigureAwait(false);
                LastUsed = p;
                return text;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                last = ex;
                ProviderFailed?.Invoke(p, ex);
            }
        }
        throw last!;
    }
}
