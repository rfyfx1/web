using System.Net;
using Rahbar.Core.Abstractions;
using Rahbar.Core.Language;
using Rahbar.Core.Net;
using Rahbar.Core.Pipeline;
using Rahbar.Core.Prompts;
using Rahbar.Core.Settings;

namespace Rahbar.Core.Tests;

public class PipelineTests
{
    private sealed class FakeStt(string name, Func<string> result) : ISpeechToText
    {
        public string Name => name;
        public int Calls;
        public (string? lang, string? prompt) LastArgs;
        public Task<string> TranscribeAsync(byte[] wav, string? languageHint, string? stylePrompt, CancellationToken ct)
        {
            Calls++;
            LastArgs = (languageHint, stylePrompt);
            return Task.FromResult(result());
        }
    }

    private sealed class FakeBrain(params string[] replies) : IVisionBrain
    {
        public string Name => "fake";
        public List<BrainRequest> Requests { get; } = new();
        public Task<BrainReply> AskAsync(BrainRequest req, CancellationToken ct)
        {
            Requests.Add(req);
            return Task.FromResult(new BrainReply(replies[Math.Min(Requests.Count - 1, replies.Length - 1)]));
        }
    }

    private static CapturedScreen Screen() => new()
    {
        Image = new byte[] { 1, 2, 3 },
        Context = new ScreenContext("Untitled - Notepad", "notepad", 1568, 882),
    };

    [Fact]
    public async Task Fallback_stt_uses_second_provider_when_first_fails()
    {
        var bad = new FakeStt("groq", () => throw new HttpRequestException("offline"));
        var good = new FakeStt("windows", () => "where is save");
        var stt = new FallbackSpeechToText(bad, good);
        Exception? reported = null;
        stt.ProviderFailed += (_, e) => reported = e;

        Assert.Equal("where is save", await stt.TranscribeAsync(new byte[1], "en", null, default));
        Assert.Equal(1, bad.Calls);
        Assert.IsType<HttpRequestException>(reported);
    }

    [Fact]
    public async Task Fallback_stt_throws_last_error_when_all_fail()
    {
        var a = new FakeStt("a", () => throw new InvalidOperationException("a"));
        var b = new FakeStt("b", () => throw new FriendlyException("b failed"));
        var ex = await Assert.ThrowsAsync<FriendlyException>(() => new FallbackSpeechToText(a, b).TranscribeAsync(new byte[1], null, null, default));
        Assert.Equal("b failed", ex.Message);
    }

    [Fact]
    public async Task Retries_once_with_strict_json_reminder_after_bad_reply()
    {
        var brain = new FakeBrain("not json at all {", """{"steps":[{"say":"Save is top left."}],"language":"en"}""");
        var p = new AskPipeline(new FakeStt("s", () => ""), brain, new ConversationMemory());

        var r = await p.AskAsync(Screen(), "where is save?", LanguageProfile.For(LanguagePreference.Auto), null, default);

        Assert.Equal(2, brain.Requests.Count);
        Assert.False(brain.Requests[0].StrictJsonReminder);
        Assert.True(brain.Requests[1].StrictJsonReminder);
        Assert.EndsWith("Return only valid JSON.", brain.Requests[1].UserText);
        Assert.Equal("Save is top left.", r.Answer.Steps[0].Say);
    }

    [Fact]
    public async Task Falls_back_to_plain_text_when_json_never_arrives()
    {
        var brain = new FakeBrain("Save button upar left mein hai.");
        var p = new AskPipeline(new FakeStt("s", () => ""), brain, new ConversationMemory());
        var r = await p.AskAsync(Screen(), "save kahan hai", LanguageProfile.For(LanguagePreference.RomanUrdu), null, default);
        Assert.Equal("Save button upar left mein hai.", r.Answer.Steps[0].Say);
    }

    [Fact]
    public async Task Empty_question_becomes_explain_screen_and_memory_is_kept()
    {
        var brain = new FakeBrain("""{"steps":[{"say":"This is Notepad."}]}""");
        var memory = new ConversationMemory();
        var p = new AskPipeline(new FakeStt("s", () => ""), brain, memory);

        var r = await p.AskAsync(Screen(), "  ", LanguageProfile.For(LanguagePreference.Auto), null, default);
        Assert.True(r.WasExplainScreen);
        Assert.Contains(PromptBuilder.ExplainScreenQuestion, brain.Requests[0].UserText);

        await p.AskAsync(Screen(), "and how do I save?", LanguageProfile.For(LanguagePreference.Auto), null, default);
        Assert.Contains("Rahbar: This is Notepad.", brain.Requests[1].UserText);
    }

    [Fact]
    public async Task Transcribe_passes_language_hint_and_romanized_style_prompt()
    {
        var stt = new FakeStt("s", () => "save ka button kahan hai");
        var p = new AskPipeline(stt, new FakeBrain("{}"), new ConversationMemory());

        var roman = LanguageProfile.For(LanguagePreference.RomanUrdu);
        Assert.Equal("save ka button kahan hai", await p.TranscribeAsync(new byte[1], true, roman, default));
        Assert.Null(stt.LastArgs.lang);
        Assert.Equal(roman.WhisperStylePrompt, stt.LastArgs.prompt);

        await p.TranscribeAsync(new byte[1], true, LanguageProfile.For(LanguagePreference.Urdu), default);
        Assert.Equal("ur", stt.LastArgs.lang);
    }

    [Fact]
    public async Task Silence_and_whisper_phantoms_become_empty_question()
    {
        var stt = new FakeStt("s", () => "Thank you.");
        var p = new AskPipeline(stt, new FakeBrain("{}"), new ConversationMemory());
        Assert.Equal("", await p.TranscribeAsync(new byte[1], true, LanguageProfile.For(LanguagePreference.Auto), default));
        Assert.Equal("", await p.TranscribeAsync(new byte[1], false, LanguageProfile.For(LanguagePreference.Auto), default));
        Assert.Equal(1, stt.Calls); // no speech → no API call
    }

    [Fact]
    public void Memory_keeps_last_three_turns()
    {
        var m = new ConversationMemory();
        for (int i = 1; i <= 5; i++) m.Add($"q{i}", $"a{i}");
        Assert.Equal(new[] { "q3", "q4", "q5" }, m.Recent().Select(t => t.Question));
    }

    [Fact]
    public void Private_mode_forces_offline_providers()
    {
        var s = new AppSettings { BrainProvider = ProviderIds.Gemini, SpeechToTextProvider = ProviderIds.Groq, PrivateMode = true };
        Assert.Equal(ProviderIds.Ollama, s.EffectiveBrain);
        Assert.Equal(ProviderIds.WindowsSpeech, s.EffectiveSpeechToText);
    }

    [Fact]
    public void Settings_round_trip_and_survive_corrupt_file()
    {
        var dir = Path.Combine(Path.GetTempPath(), "rahbar-test-" + Guid.NewGuid());
        var store = new SettingsStore(Path.Combine(dir, "settings.json"));
        store.Save(new AppSettings { Language = LanguagePreference.Hinglish, FirstRunCompleted = true });
        var loaded = store.Load();
        Assert.Equal(LanguagePreference.Hinglish, loaded.Language);
        Assert.True(loaded.FirstRunCompleted);
        Assert.DoesNotContain("ApiKey", File.ReadAllText(store.FilePath));

        File.WriteAllText(store.FilePath, "{ broken");
        Assert.False(store.Load().FirstRunCompleted);
        Directory.Delete(dir, true);
    }

    // ---- Retry policy ----

    private sealed class SequenceHandler(params Func<HttpResponseMessage>[] responses) : HttpMessageHandler
    {
        public int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct) =>
            Task.FromResult(responses[Math.Min(Calls++, responses.Length - 1)]());
    }

    private static readonly RetryPolicy NoWait = new() { Delay = (_, _) => Task.CompletedTask };

    [Fact]
    public async Task Retry_recovers_after_429()
    {
        var h = new SequenceHandler(() => new HttpResponseMessage(HttpStatusCode.TooManyRequests),
                                    () => new HttpResponseMessage(HttpStatusCode.OK));
        using var resp = await NoWait.SendAsync("Gemini", () => new HttpRequestMessage(HttpMethod.Get, "http://x"), new HttpClient(h), default);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Equal(2, h.Calls);
    }

    [Fact]
    public async Task Retry_gives_friendly_rate_limit_message_when_exhausted()
    {
        var h = new SequenceHandler(() => new HttpResponseMessage(HttpStatusCode.TooManyRequests));
        var ex = await Assert.ThrowsAsync<RateLimitException>(() =>
            NoWait.SendAsync("Gemini", () => new HttpRequestMessage(HttpMethod.Get, "http://x"), new HttpClient(h), default));
        Assert.Equal("Gemini: free limit reached, try again in a minute.", ex.Message);
        Assert.Equal(3, h.Calls);
    }

    [Fact]
    public async Task Long_retry_after_fails_fast()
    {
        var h = new SequenceHandler(() =>
        {
            var r = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            r.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(60));
            return r;
        });
        await Assert.ThrowsAsync<RateLimitException>(() =>
            NoWait.SendAsync("Groq", () => new HttpRequestMessage(HttpMethod.Get, "http://x"), new HttpClient(h), default));
        Assert.Equal(1, h.Calls);
    }

    [Fact]
    public async Task Bad_key_gives_auth_message_without_retry()
    {
        var h = new SequenceHandler(() => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        await Assert.ThrowsAsync<ProviderAuthException>(() =>
            NoWait.SendAsync("Groq", () => new HttpRequestMessage(HttpMethod.Get, "http://x"), new HttpClient(h), default));
        Assert.Equal(1, h.Calls);
    }
}
