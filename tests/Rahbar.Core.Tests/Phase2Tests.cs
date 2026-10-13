using Rahbar.Core.Abstractions;
using Rahbar.Core.Geometry;
using Rahbar.Core.Language;
using Rahbar.Core.Pipeline;
using Rahbar.Core.Prompts;
using Rahbar.Core.Storage;
using Rahbar.Core.Walkthrough;

namespace Rahbar.Core.Tests;

public class StepCommandTests
{
    [Theory]
    [InlineData("next", StepCommand.Next)]
    [InlineData("Next.", StepCommand.Next)]
    [InlineData("ok next step please", StepCommand.Next)]
    [InlineData("agla", StepCommand.Next)]
    [InlineData("Agla step batao", StepCommand.Next)]
    [InlineData("haan ho gaya", StepCommand.Next)]
    [InlineData("theek hai aage chalo", StepCommand.Next)]
    [InlineData("اگلا", StepCommand.Next)]
    [InlineData("ہو گیا، آگے", StepCommand.Next)]
    [InlineData("अगला स्टेप", StepCommand.Next)]
    [InlineData("back", StepCommand.Back)]
    [InlineData("pichla step", StepCommand.Back)]
    [InlineData("واپس", StepCommand.Back)]
    [InlineData("dobara batao", StepCommand.Repeat)]
    [InlineData("phir se", StepCommand.Repeat)]
    [InlineData("stop", StepCommand.Stop)]
    [InlineData("bas", StepCommand.Stop)]
    [InlineData("band karo", StepCommand.Stop)]
    [InlineData("बस", StepCommand.Stop)]
    public void Recognises_commands_in_many_languages(string text, StepCommand expected) =>
        Assert.Equal(expected, StepCommands.Parse(text));

    [Theory]
    [InlineData("")]
    [InlineData("what is next to the save button?")]
    [InlineData("save ka button kahan hai")]
    [InlineData("ok")]
    [InlineData("next ke baad wala button kis kaam ka hai aur ye kya karta hai")]
    public void Real_questions_are_not_commands(string text) =>
        Assert.Equal(StepCommand.None, StepCommands.Parse(text));
}

public class WalkthroughTests
{
    private static BrainAnswer Answer(int n) => new()
    {
        Language = "ur",
        Steps = Enumerable.Range(1, n).Select(i => new BrainStep { Say = $"step {i}" }).ToList(),
    };

    [Fact]
    public void Moves_forward_and_back_within_bounds()
    {
        var w = new WalkthroughSession("q", Answer(3));
        Assert.True(w.IsMultiStep);
        Assert.False(w.MoveBack());
        Assert.True(w.MoveNext());
        Assert.True(w.MoveNext());
        Assert.True(w.IsLast);
        Assert.False(w.MoveNext());
        Assert.Equal("3/3", w.Progress);
        Assert.True(w.MoveBack());
        Assert.Equal("step 2", w.Current.Say);
    }

    [Fact]
    public void Replacing_a_step_keeps_the_original_plan()
    {
        var w = new WalkthroughSession("q", Answer(2));
        w.MoveNext();
        Assert.False(w.CurrentIsFresh);
        w.ReplaceCurrent(new BrainStep { Say = "relocated" });
        Assert.True(w.CurrentIsFresh);
        Assert.Equal("relocated", w.Current.Say);
        Assert.Equal("step 2", w.Planned[1]);
    }

    [Fact]
    public void Continue_prompt_asks_for_only_the_next_step()
    {
        var t = PromptBuilder.BuildContinueText("naya column kaise add karun?", new[] { "a", "b", "c", "d" }, 1,
            new ScreenContext("x", "excel", 100, 100));
        Assert.Contains("finished step 1", t);
        Assert.Contains("Give ONLY step 2", t);
        Assert.Contains("4. d", t);
        Assert.Contains("naya column", t);
    }

    private sealed class Brain(string reply) : IVisionBrain
    {
        public string Name => "fake";
        public string? LastUserText;
        public Task<BrainReply> AskAsync(BrainRequest req, CancellationToken ct)
        {
            LastUserText = req.UserText;
            return Task.FromResult(new BrainReply(reply));
        }
    }

    private sealed class NoStt : ISpeechToText
    {
        public string Name => "none";
        public Task<string> TranscribeAsync(byte[] w, string? l, string? p, CancellationToken ct) => Task.FromResult("");
    }

    [Fact]
    public async Task Pipeline_continue_returns_relocated_step_and_counts_requests()
    {
        var brain = new Brain("""{"steps":[{"say":"Ab Insert pe click karein","point":{"x":10,"y":10}}],"language":"ur"}""");
        var p = new AskPipeline(new NoStt(), brain, new ConversationMemory());
        var counted = new List<string>();
        p.RequestMade += counted.Add;

        var screen = new CapturedScreen { Image = new byte[1], Context = new ScreenContext("x", "excel", 100, 100) };
        var step = await p.ContinueAsync(screen, "q", new[] { "a", "b" }, 1, LanguageProfile.For(LanguagePreference.RomanUrdu), null, default);

        Assert.Equal("Ab Insert pe click karein", step.Say);
        Assert.NotNull(step.Point);
        Assert.Contains("Give ONLY step 2", brain.LastUserText);
        Assert.Equal(new[] { "fake" }, counted);
    }
}

public class TargetHitTestTests
{
    [Fact]
    public void Circle_hit_uses_dpi_scaled_radius_on_negative_monitor()
    {
        var m = CoordinateMapper.ForCapture(new MonitorInfo(new PixelRect(-1536, 0, 1536, 864), 1.5));
        var t = new PointTarget { X = 100, Y = 100, Type = PointType.Circle };
        // image (100,100) → physical (-1436, 100); radius 36 DIP × 1.5 = 54 px
        Assert.True(TargetHitTest.IsHit(t, m, -1436 + 50, 100));
        Assert.False(TargetHitTest.IsHit(t, m, -1436 + 60, 100));
        Assert.False(TargetHitTest.IsHit(t, m, 10, 100)); // other monitor
    }

    [Fact]
    public void Box_hit_covers_box_area_after_downscale()
    {
        var m = CoordinateMapper.ForCapture(new MonitorInfo(new PixelRect(0, 0, 3840, 2160), 1.0));
        var t = new PointTarget { X = 784, Y = 441, Type = PointType.Box, W = 200, H = 40 };
        // box is 200/0.4083 ≈ 490 px wide around physical (1920,1080)
        Assert.True(TargetHitTest.IsHit(t, m, 1920 + 240, 1080));
        Assert.False(TargetHitTest.IsHit(t, m, 1920 + 260, 1080));
    }
}

public class StorageTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "rahbar-p2-" + Guid.NewGuid());
    public void Dispose() { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); }

    [Fact]
    public void History_keeps_newest_50_persists_and_searches()
    {
        var path = Path.Combine(_dir, "history.json");
        var h = new HistoryStore(path);
        for (int i = 1; i <= 55; i++) h.Add(new HistoryEntry(DateTime.Now, $"q{i}", i == 55 ? "Insert tab pe jao" : $"a{i}", "ur", "EXCEL"));

        var reloaded = new HistoryStore(path);
        var all = reloaded.Search(null);
        Assert.Equal(50, all.Count);
        Assert.Equal("q55", all[0].Question);
        Assert.Single(reloaded.Search("insert TAB"));
        Assert.Equal(50, reloaded.Search("excel").Count);
    }

    [Fact]
    public void Usage_counter_counts_per_provider_and_resets_daily()
    {
        var now = new DateTime(2026, 10, 13, 9, 0, 0);
        var path = Path.Combine(_dir, "usage.json");
        var u = new UsageCounter(path, () => now);
        u.Increment("Gemini"); u.Increment("Gemini"); u.Increment("Groq Whisper");
        Assert.Equal(2, new UsageCounter(path, () => now).TodayCounts()["Gemini"]);
        Assert.Equal("Today: Gemini 2 · Groq Whisper 1", u.Summary());

        now = now.AddDays(1);
        Assert.Empty(u.TodayCounts());
    }

    [Fact]
    public void Skills_list_read_cap_and_reject_path_tricks()
    {
        var s = new SkillStore(Path.Combine(_dir, "skills"));
        s.EnsureExamples();
        Assert.Contains("Seller Central guide", s.List());
        Assert.Contains("ASIN", s.Read("Seller Central guide"));

        File.WriteAllText(Path.Combine(s.Folder, "big.md"), new string('x', 20000));
        Assert.Equal(SkillStore.MaxChars, s.Read("big")!.Length);
        Assert.Null(s.Read("../settings"));
        Assert.Null(s.Read("missing"));

        s.EnsureExamples(); // doesn't overwrite once files exist
        Assert.Equal(3, s.List().Count);
    }
}
