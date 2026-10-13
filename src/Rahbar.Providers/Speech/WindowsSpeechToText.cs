using System.Globalization;
using System.Speech.Recognition;
using System.Text;
using Rahbar.Core.Abstractions;

namespace Rahbar.Providers.Speech;

/// <summary>
/// Offline fallback using the speech recognisers built into Windows. Accuracy is lower than
/// Whisper and it only understands languages whose recogniser is installed (usually English).
/// </summary>
public sealed class WindowsSpeechToText : ISpeechToText, ITestableProvider
{
    public string Name => "Windows speech recognition (offline)";

    public Task<string> TranscribeAsync(byte[] wav, string? languageHint, string? stylePrompt, CancellationToken ct) =>
        Task.Run(() =>
        {
            var info = PickRecognizer(languageHint)
                ?? throw new FriendlyException("No offline speech recogniser is installed. Add an English speech pack in Windows Settings › Time & language › Speech.");
            using var engine = new SpeechRecognitionEngine(info);
            engine.LoadGrammar(new DictationGrammar());
            engine.SetInputToWaveStream(new MemoryStream(wav));

            var sb = new StringBuilder();
            using var reg = ct.Register(() => engine.RecognizeAsyncCancel());
            RecognitionResult? r;
            while (!ct.IsCancellationRequested && (r = engine.Recognize()) is not null)
                sb.Append(r.Text).Append(' ');
            ct.ThrowIfCancellationRequested();
            return sb.ToString().Trim();
        }, ct);

    public Task<string> TestAsync(CancellationToken ct)
    {
        var all = SpeechRecognitionEngine.InstalledRecognizers();
        if (all.Count == 0) throw new FriendlyException("No offline speech recogniser is installed.");
        return Task.FromResult("OK – offline recognisers: " + string.Join(", ", all.Select(r => r.Culture.Name)));
    }

    private static RecognizerInfo? PickRecognizer(string? languageHint)
    {
        var all = SpeechRecognitionEngine.InstalledRecognizers();
        if (all.Count == 0) return null;
        if (!string.IsNullOrEmpty(languageHint))
        {
            var match = all.FirstOrDefault(r => r.Culture.TwoLetterISOLanguageName == languageHint);
            if (match is not null) return match;
        }
        var ui = CultureInfo.CurrentUICulture.Name;
        return all.FirstOrDefault(r => r.Culture.Name == ui)
            ?? all.FirstOrDefault(r => r.Culture.TwoLetterISOLanguageName == "en")
            ?? all[0];
    }
}
