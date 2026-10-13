using Rahbar.Core.Abstractions;
using Rahbar.Core.Parsing;

namespace Rahbar.Core.Tests;

public class BrainReplyParserTests
{
    [Fact]
    public void Parses_spec_shape()
    {
        const string raw = """
        {"steps":[{"say":"Click Save at the top left.","point":{"x":40,"y":60,"type":"circle","w":0,"h":0,"label":"Save"}}],
         "language":"en","needs_more_steps":false}
        """;
        Assert.True(BrainReplyParser.TryParse(raw, 1568, 882, out var a, out _));
        var s = Assert.Single(a.Steps);
        Assert.Equal("Click Save at the top left.", s.Say);
        Assert.Equal(40, s.Point!.X);
        Assert.Equal(PointType.Circle, s.Point.Type);
        Assert.Equal("Save", s.Point.Label);
        Assert.Equal("en", a.Language);
    }

    [Fact]
    public void Handles_code_fences_and_chatter()
    {
        const string raw = "Sure! Here you go:\n```json\n{\"steps\":[{\"say\":\"Hi {there}\",\"point\":null}],\"language\":\"en\"}\n```";
        Assert.True(BrainReplyParser.TryParse(raw, 100, 100, out var a, out _));
        Assert.Equal("Hi {there}", a.Steps[0].Say);
        Assert.Null(a.Steps[0].Point);
    }

    [Fact]
    public void Parses_urdu_and_roman_urdu_with_native_speak_line()
    {
        const string raw = """
        {"steps":[{"say":"Upar left mein File pe click karein.","speak":"اوپر بائیں طرف File پر کلک کریں۔","speak_language":"ur",
                   "point":{"x":"20","y":"15","type":"box","w":"60","h":"24"}}],"language":"ur"}
        """;
        Assert.True(BrainReplyParser.TryParse(raw, 1000, 800, out var a, out _));
        var s = a.Steps[0];
        Assert.Equal("ur", a.Language);
        Assert.Equal("ur", s.SpeakLanguage);
        Assert.StartsWith("اوپر", s.Speak);
        Assert.Equal(PointType.Box, s.Point!.Type);
        Assert.Equal(60, s.Point.W);
    }

    [Fact]
    public void Drops_points_far_off_screen_and_clamps_near_edge()
    {
        const string raw = """{"steps":[{"say":"a","point":{"x":5000,"y":10}},{"say":"b","point":{"x":1003,"y":-3}}]}""";
        Assert.True(BrainReplyParser.TryParse(raw, 1000, 800, out var a, out _));
        Assert.Null(a.Steps[0].Point);
        Assert.Equal(999, a.Steps[1].Point!.X);
        Assert.Equal(0, a.Steps[1].Point!.Y);
    }

    [Fact]
    public void Scales_fractional_coordinates()
    {
        const string raw = """{"steps":[{"say":"a","point":{"x":0.5,"y":0.25}}]}""";
        Assert.True(BrainReplyParser.TryParse(raw, 1000, 800, out var a, out _));
        Assert.Equal(500, a.Steps[0].Point!.X);
        Assert.Equal(200, a.Steps[0].Point!.Y);
    }

    [Fact]
    public void Caps_steps_at_six()
    {
        var steps = string.Join(",", Enumerable.Range(1, 9).Select(i => $"{{\"say\":\"step {i}\"}}"));
        Assert.True(BrainReplyParser.TryParse($"{{\"steps\":[{steps}]}}", 10, 10, out var a, out _));
        Assert.Equal(6, a.Steps.Count);
    }

    [Fact]
    public void Accepts_single_top_level_step()
    {
        Assert.True(BrainReplyParser.TryParse("""{"say":"Yeh Excel ki sheet hai.","language":"ur"}""", 10, 10, out var a, out _));
        Assert.Equal("Yeh Excel ki sheet hai.", a.Steps[0].Say);
    }

    [Theory]
    [InlineData("")]
    [InlineData("I cannot help with that.")]
    [InlineData("{\"steps\":[]}")]
    [InlineData("{\"steps\":[{\"say\":\"unterminated")]
    public void Rejects_unusable_replies(string raw)
    {
        Assert.False(BrainReplyParser.TryParse(raw, 10, 10, out _, out var err));
        Assert.NotNull(err);
    }

    [Fact]
    public void Normalises_language_tags()
    {
        Assert.True(BrainReplyParser.TryParse("""{"say":"x","language":"hi-Latn"}""", 10, 10, out var a, out _));
        Assert.Equal("hi", a.Language);
    }
}
