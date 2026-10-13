using Rahbar.Core.Abstractions;
using Rahbar.Core.Geometry;
using Rahbar.Core.Language;
using Rahbar.Core.Prompts;

namespace Rahbar.Core.Tests;

public class LanguageTests
{
    private static readonly VoiceInfo UsEnglish = new("Microsoft David", "en-US");
    private static readonly VoiceInfo IndianEnglish = new("Microsoft Heera", "en-IN");
    private static readonly VoiceInfo Urdu = new("Microsoft Uzma", "ur-PK");
    private static readonly VoiceInfo Hindi = new("Microsoft Kalpana", "hi-IN");
    private static readonly VoiceInfo[] All = { UsEnglish, IndianEnglish, Urdu, Hindi };

    [Theory]
    [InlineData("Where is the save button?", TextScript.Latin)]
    [InlineData("save ka button kahan hai?", TextScript.Latin)]
    [InlineData("محفوظ کرنے کا بٹن کہاں ہے؟", TextScript.Arabic)]
    [InlineData("सेव बटन कहाँ है?", TextScript.Devanagari)]
    [InlineData("File پر کلک کریں اور پھر Save دبائیں", TextScript.Arabic)]
    [InlineData("   ", TextScript.Unknown)]
    public void Detects_script(string text, TextScript expected) =>
        Assert.Equal(expected, ScriptDetector.Detect(text));

    [Fact]
    public void Roman_urdu_uses_native_urdu_voice_when_brain_gave_native_text()
    {
        var step = new BrainStep { Say = "File pe click karein.", Speak = "File پر کلک کریں۔", SpeakLanguage = "ur" };
        var line = VoicePicker.PlanLine(step, "ur", All);
        Assert.Equal("File پر کلک کریں۔", line.Text);
        Assert.Equal(Urdu, VoicePicker.Pick(line, All));
    }

    [Fact]
    public void Roman_urdu_falls_back_to_indian_english_voice_without_urdu_voice()
    {
        var voices = new[] { UsEnglish, IndianEnglish };
        var step = new BrainStep { Say = "File pe click karein.", Speak = "File پر کلک کریں۔", SpeakLanguage = "ur" };
        var line = VoicePicker.PlanLine(step, "ur", voices);
        Assert.Equal("File pe click karein.", line.Text);
        Assert.Equal(IndianEnglish, VoicePicker.Pick(line, voices));
    }

    [Fact]
    public void Hinglish_without_native_text_uses_indian_english_voice()
    {
        var line = VoicePicker.PlanLine(new BrainStep { Say = "Top left pe File menu kholo." }, "hi", All);
        Assert.Equal(IndianEnglish, VoicePicker.Pick(line, All));
    }

    [Fact]
    public void Urdu_script_picks_urdu_voice_and_hindi_script_picks_hindi_voice()
    {
        Assert.Equal(Urdu, VoicePicker.Pick(new SpeechLine("یہاں کلک کریں", null), All));
        Assert.Equal(Hindi, VoicePicker.Pick(new SpeechLine("यहाँ क्लिक करें", null), All));
    }

    [Fact]
    public void Plain_english_respects_preferred_english_voice()
    {
        var line = VoicePicker.PlanLine(new BrainStep { Say = "Click Save." }, "en", All);
        Assert.Equal(IndianEnglish, VoicePicker.Pick(line, All, "Microsoft Heera"));
        Assert.Equal(UsEnglish, VoicePicker.Pick(line, All));
    }

    [Fact]
    public void Missing_native_voice_returns_null_for_native_script()
    {
        Assert.Null(VoicePicker.Pick(new SpeechLine("یہاں کلک کریں", "ur"), new[] { UsEnglish }));
    }

    [Fact]
    public void Auto_profile_mirrors_any_language_and_mixing()
    {
        var p = LanguageProfile.For(LanguagePreference.Auto);
        Assert.Null(p.WhisperLanguage); // let Whisper detect
        Assert.Contains("same language, script and mix", p.ReplyRule);
        Assert.Contains("Hinglish", p.ReplyRule);
    }

    [Theory]
    [InlineData(LanguagePreference.English, "en")]
    [InlineData(LanguagePreference.Urdu, "ur")]
    [InlineData(LanguagePreference.Hindi, "hi")]
    [InlineData(LanguagePreference.RomanUrdu, null)]
    [InlineData(LanguagePreference.Hinglish, null)]
    public void Whisper_hint_per_preference(LanguagePreference pref, string? code)
    {
        var p = LanguageProfile.For(pref);
        Assert.Equal(code, p.WhisperLanguage);
        if (p.IsRomanized) Assert.NotNull(p.WhisperStylePrompt);
    }

    [Fact]
    public void System_prompt_fills_placeholders_and_language_rule()
    {
        var ctx = new ScreenContext("Book1 - Excel", "EXCEL", 1568, 882);
        var s = PromptBuilder.BuildSystemPrompt(ctx, LanguageProfile.For(LanguagePreference.Hinglish), "Be extra brief.");
        Assert.Contains("Book1 - Excel (EXCEL)", s);
        Assert.Contains("width 1568, height 882", s);
        Assert.Contains("Hinglish", s);
        Assert.Contains("Be extra brief.", s);
        Assert.DoesNotContain("{language_rule}", s);
        Assert.DoesNotContain("{w}", s);
    }

    [Fact]
    public void User_text_includes_memory_and_explain_fallback()
    {
        var ctx = new ScreenContext("x", "y", 100, 100, 10, 20);
        var t = PromptBuilder.BuildUserText("", ctx, new[] { new ConversationTurn("q1", "a1") });
        Assert.Contains("User: q1", t);
        Assert.Contains("(10, 20)", t);
        Assert.Contains(PromptBuilder.ExplainScreenQuestion, t);
    }
}
