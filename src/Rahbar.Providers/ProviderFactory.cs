using Rahbar.Core.Abstractions;
using Rahbar.Core.Pipeline;
using Rahbar.Core.Settings;
using Rahbar.Providers.Brains;
using Rahbar.Providers.Speech;

namespace Rahbar.Providers;

/// <summary>Builds the providers chosen in Settings. The rest of the app only sees the interfaces.</summary>
public sealed class ProviderFactory
{
    private readonly HttpClient _http;
    private readonly ISecretStore _secrets;

    public ProviderFactory(HttpClient http, ISecretStore secrets)
    {
        _http = http;
        _secrets = secrets;
    }

    public IVisionBrain CreateBrain(AppSettings s, string? overrideId = null) => (overrideId ?? s.EffectiveBrain) switch
    {
        ProviderIds.Claude => new ClaudeVisionBrain(_http, Key(SecretNames.Claude, "Claude"), s.ClaudeModel),
        ProviderIds.Ollama => new OllamaVisionBrain(_http, s.OllamaUrl, s.OllamaModel),
        _ => new GeminiVisionBrain(_http, Key(SecretNames.Gemini, "Gemini"), s.GeminiModel),
    };

    /// <summary>Groq first (best for Urdu/Hindi/mixed speech), then offline Windows recognition as backup.</summary>
    public ISpeechToText CreateSpeechToText(AppSettings s, Action<ISpeechToText, Exception>? onFallback = null)
    {
        var offline = new WindowsSpeechToText();
        if (s.EffectiveSpeechToText == ProviderIds.WindowsSpeech) return offline;

        var groqKey = _secrets.Get(SecretNames.Groq);
        if (string.IsNullOrWhiteSpace(groqKey)) return offline;

        var chain = new FallbackSpeechToText(new GroqSpeechToText(_http, groqKey, s.GroqModel), offline);
        if (onFallback is not null) chain.ProviderFailed += onFallback;
        return chain;
    }

    public ISpeechToText CreateGroq(AppSettings s) =>
        new GroqSpeechToText(_http, Key(SecretNames.Groq, "Groq"), s.GroqModel);

    public ITextToSpeech CreateTextToSpeech(AppSettings s) => new WindowsTextToSpeech(s.PreferredVoice, s.SpeechRate);

    private string Key(string name, string provider)
    {
        var k = _secrets.Get(name);
        if (string.IsNullOrWhiteSpace(k))
            throw new FriendlyException($"No {provider} key yet. Open Settings from the tray icon and paste your key.");
        return k.Trim();
    }
}
