using System.Windows;
using System.Windows.Controls;
using Rahbar.Core.Abstractions;
using Rahbar.Core.Language;
using Rahbar.Core.Settings;
using Rahbar.Providers;
using Rahbar.Providers.Speech;

namespace Rahbar.App.Windows;

public partial class SettingsWindow : Window
{
    private const string AutoVoice = "Automatic (match each answer's language)";
    private readonly AppSettings _settings;
    private readonly ISecretStore _secrets;
    private readonly PendingSecrets _pending;
    private readonly ProviderFactory _testFactory;
    private readonly IReadOnlyList<VoiceInfo> _voices;

    public event Action? Saved;

    internal SettingsWindow(AppSettings settings, ISecretStore secrets, ProviderFactory factory)
    {
        InitializeComponent();
        _settings = settings;
        _secrets = secrets;
        _pending = new PendingSecrets(secrets);
        _testFactory = new ProviderFactory(new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(20) }, _pending);

        LanguageBox.ItemsSource = LanguageProfile.All;
        LanguageBox.SelectedItem = LanguageProfile.All.First(p => p.Preference == settings.Language);
        BrainBox.SelectedValue = settings.BrainProvider;
        SttBox.SelectedValue = settings.SpeechToTextProvider;

        GeminiKey.Password = secrets.Get(SecretNames.Gemini) ?? "";
        ClaudeKey.Password = secrets.Get(SecretNames.Claude) ?? "";
        GroqKey.Password = secrets.Get(SecretNames.Groq) ?? "";
        GeminiModel.Text = settings.GeminiModel;
        ClaudeModel.Text = settings.ClaudeModel;
        OllamaUrl.Text = settings.OllamaUrl;
        OllamaModel.Text = settings.OllamaModel;
        GroqModel.Text = settings.GroqModel;

        try { _voices = WindowsTextToSpeech.InstalledVoices(); }
        catch { _voices = Array.Empty<VoiceInfo>(); }
        VoiceBox.Items.Add(AutoVoice);
        foreach (var v in _voices) VoiceBox.Items.Add($"{v.Name} ({v.LanguageTag})");
        var idx = _voices.ToList().FindIndex(v => v.Name == settings.PreferredVoice);
        VoiceBox.SelectedIndex = idx < 0 ? 0 : idx + 1;
        RateSlider.Value = settings.SpeechRate;

        HotkeyInput.Hotkey = Copy(settings.Hotkey);
        TypedHotkeyInput.Hotkey = Copy(settings.TypedHotkey);
        DictationHotkeyInput.Hotkey = Copy(settings.DictationHotkey);
        ClickAdvanceBox.IsChecked = settings.AdvanceOnTargetClick;
        FreshShotBox.IsChecked = settings.FreshScreenshotPerStep;
        UpdateUrlBox.Text = settings.UpdateUrl;
        PrivateBox.IsChecked = settings.PrivateMode;
        DebugBox.IsChecked = settings.DebugMode;
    }

    /// <summary>Copies what's on screen into a settings object (used for Save and for Test).</summary>
    private AppSettings Snapshot()
    {
        var s = new AppSettings
        {
            BrainProvider = (string?)BrainBox.SelectedValue ?? ProviderIds.Gemini,
            SpeechToTextProvider = (string?)SttBox.SelectedValue ?? ProviderIds.Groq,
            GeminiModel = Or(GeminiModel.Text, _settings.GeminiModel),
            ClaudeModel = Or(ClaudeModel.Text, _settings.ClaudeModel),
            OllamaUrl = Or(OllamaUrl.Text, _settings.OllamaUrl),
            OllamaModel = Or(OllamaModel.Text, _settings.OllamaModel),
            GroqModel = Or(GroqModel.Text, _settings.GroqModel),
            SpeechRate = RateSlider.Value,
            PreferredVoice = VoiceBox.SelectedIndex > 0 ? _voices[VoiceBox.SelectedIndex - 1].Name : null,
        };
        _pending.Set(SecretNames.Gemini, GeminiKey.Password);
        _pending.Set(SecretNames.Claude, ClaudeKey.Password);
        _pending.Set(SecretNames.Groq, GroqKey.Password);
        return s;
    }

    private static HotkeySettings Copy(HotkeySettings h) =>
        new() { Ctrl = h.Ctrl, Shift = h.Shift, Alt = h.Alt, Win = h.Win, Key = h.Key };

    private static string Or(string? text, string fallback) => string.IsNullOrWhiteSpace(text) ? fallback : text.Trim();

    private async void GeminiTest_Click(object sender, RoutedEventArgs e) =>
        await UiHelpers.RunTestAsync(GeminiTest, GeminiResult, () => (ITestableProvider)_testFactory.CreateBrain(Snapshot(), ProviderIds.Gemini));

    private async void ClaudeTest_Click(object sender, RoutedEventArgs e) =>
        await UiHelpers.RunTestAsync(ClaudeTest, ClaudeResult, () => (ITestableProvider)_testFactory.CreateBrain(Snapshot(), ProviderIds.Claude));

    private async void OllamaTest_Click(object sender, RoutedEventArgs e) =>
        await UiHelpers.RunTestAsync(OllamaTest, OllamaResult, () => (ITestableProvider)_testFactory.CreateBrain(Snapshot(), ProviderIds.Ollama));

    private async void GroqTest_Click(object sender, RoutedEventArgs e) =>
        await UiHelpers.RunTestAsync(GroqTest, GroqResult, () => (ITestableProvider)_testFactory.CreateGroq(Snapshot()));

    private async void VoiceTest_Click(object sender, RoutedEventArgs e)
    {
        var s = Snapshot();
        var pref = ((LanguageProfile?)LanguageBox.SelectedItem)?.Preference ?? LanguagePreference.Auto;
        var (step, lang) = UiHelpers.VoiceSample(pref);
        using var tts = new WindowsTextToSpeech(s.PreferredVoice, s.SpeechRate);
        var line = VoicePicker.PlanLine(step, lang, tts.Voices);
        if (!await tts.SpeakAsync(line, CancellationToken.None))
            MessageBox.Show(this, "No voice for this language is installed on this PC. Answers will still show as text.\n\n" +
                                  "To add one: Windows Settings › Time & language › Language & region › add the language › tick 'Speech'.", "Rahbar");
    }

    private void GeminiLink_Click(object sender, RoutedEventArgs e) => UiHelpers.OpenLink("https://aistudio.google.com/apikey");
    private void ClaudeLink_Click(object sender, RoutedEventArgs e) => UiHelpers.OpenLink("https://console.anthropic.com/settings/keys");
    private void GroqLink_Click(object sender, RoutedEventArgs e) => UiHelpers.OpenLink("https://console.groq.com/keys");

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var keys = new[] { HotkeyInput.Hotkey.ToString(), TypedHotkeyInput.Hotkey.ToString(), DictationHotkeyInput.Hotkey.ToString() };
        if (keys.Distinct().Count() < keys.Length)
        {
            MessageBox.Show(this, "Two hotkeys are the same. Please give each one different keys.", "Rahbar");
            return;
        }

        var s = Snapshot();
        try
        {
            _secrets.Set(SecretNames.Gemini, GeminiKey.Password);
            _secrets.Set(SecretNames.Claude, ClaudeKey.Password);
            _secrets.Set(SecretNames.Groq, GroqKey.Password);
        }
        catch (FriendlyException ex)
        {
            MessageBox.Show(this, ex.Message, "Rahbar");
            return;
        }

        _settings.Language = ((LanguageProfile?)LanguageBox.SelectedItem)?.Preference ?? LanguagePreference.Auto;
        _settings.BrainProvider = s.BrainProvider;
        _settings.SpeechToTextProvider = s.SpeechToTextProvider;
        _settings.GeminiModel = s.GeminiModel;
        _settings.ClaudeModel = s.ClaudeModel;
        _settings.OllamaUrl = s.OllamaUrl;
        _settings.OllamaModel = s.OllamaModel;
        _settings.GroqModel = s.GroqModel;
        _settings.PreferredVoice = s.PreferredVoice;
        _settings.SpeechRate = s.SpeechRate;
        _settings.Hotkey = HotkeyInput.Hotkey;
        _settings.TypedHotkey = TypedHotkeyInput.Hotkey;
        _settings.DictationHotkey = DictationHotkeyInput.Hotkey;
        _settings.AdvanceOnTargetClick = ClickAdvanceBox.IsChecked == true;
        _settings.FreshScreenshotPerStep = FreshShotBox.IsChecked == true;
        _settings.UpdateUrl = UpdateUrlBox.Text.Trim();
        _settings.PrivateMode = PrivateBox.IsChecked == true;
        _settings.DebugMode = DebugBox.IsChecked == true;
        Saved?.Invoke();
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();
}
