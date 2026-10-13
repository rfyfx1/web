using System.Windows;
using Rahbar.App.Audio;
using Rahbar.Core.Abstractions;
using Rahbar.Core.Language;
using Rahbar.Core.Settings;
using Rahbar.Providers;
using Serilog;

namespace Rahbar.App.Windows;

public partial class FirstRunWindow : Window
{
    private static readonly string[] Titles = { "Choose your language", "Gemini key (the brain)", "Groq key (your voice)", "Test your microphone", "Pick your hotkey", "Done" };
    private readonly UIElement[] _pages;
    private readonly AppSettings _settings;
    private readonly ISecretStore _secrets;
    private readonly PendingSecrets _pending;
    private readonly ProviderFactory _testFactory;
    private int _page;

    public bool Completed { get; private set; }

    internal FirstRunWindow(AppSettings settings, ISecretStore secrets, ProviderFactory factory)
    {
        InitializeComponent();
        _settings = settings;
        _secrets = secrets;
        _pending = new PendingSecrets(secrets);
        _testFactory = new ProviderFactory(new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(20) }, _pending);
        _pages = new UIElement[] { PageLanguage, PageGemini, PageGroq, PageMic, PageHotkey, PageDone };

        LanguageList.ItemsSource = LanguageProfile.All;
        LanguageList.SelectedItem = LanguageProfile.All.First(p => p.Preference == settings.Language);
        GeminiKey.Password = secrets.Get(SecretNames.Gemini) ?? "";
        GroqKey.Password = secrets.Get(SecretNames.Groq) ?? "";
        HotkeyInput.Hotkey = settings.Hotkey;
        ShowPage(0);
    }

    private void ShowPage(int i)
    {
        _page = i;
        for (int p = 0; p < _pages.Length; p++) _pages[p].Visibility = p == i ? Visibility.Visible : Visibility.Collapsed;
        StepTitle.Text = Titles[i];
        StepCounter.Text = $"Step {i + 1} of {_pages.Length}";
        BackButton.Visibility = i == 0 ? Visibility.Hidden : Visibility.Visible;
        NextButton.Content = i == _pages.Length - 1 ? "Start using Rahbar" : "Next";

        if (i == _pages.Length - 1)
        {
            var hk = HotkeyInput.Hotkey;
            DoneText.Text =
                $"How to use Rahbar:\n" +
                $"• Hold {hk}, ask your question out loud, then let go.\n" +
                $"• Just tap {hk} (without speaking) to hear what's on your screen.\n" +
                $"• Press Esc to stop talking and clear the screen.\n\n" +
                $"Try it now: open Notepad, hold {hk} and say \"Where is the save button?\" or \"Save ka button kahan hai?\"\n\n" +
                "Rahbar lives in the tray near the clock. Right-click its icon for Settings, Language, Private mode and Pause.";
        }
    }

    private void Next_Click(object sender, RoutedEventArgs e)
    {
        if (_page == 1 && string.IsNullOrWhiteSpace(GeminiKey.Password) &&
            MessageBox.Show(this, "Without a Gemini key Rahbar can't see your screen (unless you use Ollama). Continue anyway?",
                "Rahbar", MessageBoxButton.YesNo) != MessageBoxResult.Yes)
            return;

        if (_page < _pages.Length - 1)
        {
            ShowPage(_page + 1);
            return;
        }

        try
        {
            _secrets.Set(SecretNames.Gemini, GeminiKey.Password);
            _secrets.Set(SecretNames.Groq, GroqKey.Password);
        }
        catch (FriendlyException ex)
        {
            MessageBox.Show(this, ex.Message, "Rahbar");
            return;
        }
        _settings.Language = ((LanguageProfile?)LanguageList.SelectedItem)?.Preference ?? LanguagePreference.Auto;
        _settings.Hotkey = HotkeyInput.Hotkey;
        if (string.IsNullOrWhiteSpace(GroqKey.Password)) _settings.SpeechToTextProvider = ProviderIds.WindowsSpeech;
        Completed = true;
        Close();
    }

    private void Back_Click(object sender, RoutedEventArgs e)
    {
        if (_page > 0) ShowPage(_page - 1);
    }

    private void SyncPending()
    {
        _pending.Set(SecretNames.Gemini, GeminiKey.Password);
        _pending.Set(SecretNames.Groq, GroqKey.Password);
    }

    private async void GeminiTest_Click(object sender, RoutedEventArgs e)
    {
        SyncPending();
        await UiHelpers.RunTestAsync(GeminiTest, GeminiResult, () => (ITestableProvider)_testFactory.CreateBrain(_settings, ProviderIds.Gemini));
    }

    private async void GroqTest_Click(object sender, RoutedEventArgs e)
    {
        SyncPending();
        await UiHelpers.RunTestAsync(GroqTest, GroqResult, () => (ITestableProvider)_testFactory.CreateGroq(_settings));
    }

    private async void Mic_Click(object sender, RoutedEventArgs e)
    {
        MicButton.IsEnabled = false;
        MicResult.Text = "Listening… speak now";
        using var mic = new MicRecorder();
        mic.Level += level => Dispatcher.BeginInvoke(() => MicLevel.Value = level);
        try
        {
            mic.Start();
            await Task.Delay(4000);
            var rec = await mic.StopAsync();
            MicLevel.Value = 0;
            if (!rec.HasSpeech)
            {
                MicResult.Text = "I didn't hear anything. Check that the right microphone is selected in Windows sound settings, then try again.";
                return;
            }
            if (string.IsNullOrWhiteSpace(GroqKey.Password))
            {
                MicResult.Text = "✔ Your microphone works. (Add a Groq key on the previous step to check speech recognition.)";
                return;
            }

            SyncPending();
            MicResult.Text = "Understanding…";
            var lang = LanguageProfile.For(((LanguageProfile?)LanguageList.SelectedItem)?.Preference ?? LanguagePreference.Auto);
            var text = await _testFactory.CreateGroq(_settings).TranscribeAsync(rec.Wav, lang.WhisperLanguage, lang.WhisperStylePrompt, CancellationToken.None);
            MicResult.Text = string.IsNullOrWhiteSpace(text) ? "I heard sound but no words. Try again a bit closer to the mic." : $"✔ I heard: \"{text.Trim()}\"";
        }
        catch (FriendlyException ex)
        {
            MicResult.Text = "✖ " + ex.Message;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Mic test failed");
            MicResult.Text = "✖ Microphone test failed: " + ex.Message;
        }
        finally
        {
            MicButton.IsEnabled = true;
        }
    }

    private void GeminiLink_Click(object sender, RoutedEventArgs e) => UiHelpers.OpenLink("https://aistudio.google.com/apikey");
    private void GroqLink_Click(object sender, RoutedEventArgs e) => UiHelpers.OpenLink("https://console.groq.com/keys");
}
