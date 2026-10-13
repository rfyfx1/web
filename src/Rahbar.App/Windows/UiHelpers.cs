using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Rahbar.Core.Abstractions;
using Rahbar.Core.Language;
using Serilog;

namespace Rahbar.App.Windows;

/// <summary>Lets "Test" buttons use the key typed in the box before it is saved.</summary>
internal sealed class PendingSecrets : ISecretStore
{
    private readonly ISecretStore _inner;
    private readonly Dictionary<string, string?> _pending = new();
    public PendingSecrets(ISecretStore inner) => _inner = inner;
    public string? Get(string name) => _pending.TryGetValue(name, out var v) ? v : _inner.Get(name);
    public void Set(string name, string? value) => _pending[name] = value;
}

internal static class UiHelpers
{
    public static void OpenLink(string url) =>
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });

    /// <summary>Runs a provider's Test and shows ✔/✖ with a friendly message.</summary>
    public static async Task RunTestAsync(Button button, TextBlock result, Func<ITestableProvider> make)
    {
        button.IsEnabled = false;
        result.Foreground = Brushes.Gray;
        result.Text = "Testing…";
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            var msg = await make().TestAsync(cts.Token);
            result.Foreground = new SolidColorBrush(Color.FromRgb(0x05, 0x96, 0x69));
            result.Text = "✔ " + msg;
        }
        catch (Exception ex)
        {
            if (ex is not FriendlyException) Log.Warning(ex, "Provider test failed");
            result.Foreground = new SolidColorBrush(Color.FromRgb(0xDC, 0x26, 0x26));
            result.Text = "✖ " + (ex is FriendlyException ? ex.Message : "Test failed: " + ex.Message);
        }
        finally
        {
            button.IsEnabled = true;
        }
    }

    /// <summary>A short greeting in each language, for the "Test voice" button.</summary>
    public static (BrainStep Step, string Lang) VoiceSample(LanguagePreference p) => p switch
    {
        LanguagePreference.Urdu => (new BrainStep { Say = "السلام علیکم، میں رہبر ہوں۔ اسکرین کے بارے میں کچھ بھی پوچھیں۔" }, "ur"),
        LanguagePreference.RomanUrdu => (new BrainStep
        {
            Say = "Assalam o alaikum, main Rahbar hoon. Screen ke baare mein kuch bhi poochein.",
            Speak = "السلام علیکم، میں رہبر ہوں۔ اسکرین کے بارے میں کچھ بھی پوچھیں۔", SpeakLanguage = "ur",
        }, "ur"),
        LanguagePreference.Hindi => (new BrainStep { Say = "नमस्ते, मैं रहबर हूँ। स्क्रीन के बारे में कुछ भी पूछिए।" }, "hi"),
        LanguagePreference.Hinglish => (new BrainStep
        {
            Say = "Hello, main Rahbar hoon. Screen ke baare mein kuch bhi poocho.",
            Speak = "हेलो, मैं रहबर हूँ। स्क्रीन के बारे में कुछ भी पूछो।", SpeakLanguage = "hi",
        }, "hi"),
        _ => (new BrainStep { Say = "Hi, I'm Rahbar. Hold the hotkey and ask me anything about your screen." }, "en"),
    };
}
