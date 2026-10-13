using Rahbar.Core.Abstractions;

namespace Rahbar.Core.Language;

/// <summary>An installed text-to-speech voice, e.g. ("Microsoft Uzma", "ur-PK").</summary>
public sealed record VoiceInfo(string Name, string LanguageTag)
{
    public string LanguageCode => LanguageTag.Split('-')[0].ToLowerInvariant();
}

/// <summary>Decides what text to speak for a step and which installed voice should say it.</summary>
public static class VoicePicker
{
    /// <summary>
    /// Romanized Urdu/Hindi sounds wrong in a US voice. If the brain also gave the sentence in
    /// native script and a matching voice is installed, speak that; otherwise speak the caption.
    /// </summary>
    public static SpeechLine PlanLine(BrainStep step, string replyLanguage, IReadOnlyList<VoiceInfo> voices)
    {
        if (!string.IsNullOrWhiteSpace(step.Speak))
        {
            var lang = step.SpeakLanguage ?? LanguageForScript(ScriptDetector.Detect(step.Speak), replyLanguage);
            if (lang is not null && voices.Any(v => v.LanguageCode == lang))
                return new SpeechLine(step.Speak!, lang);
        }

        var script = ScriptDetector.Detect(step.Say);
        var code = script == TextScript.Latin && replyLanguage is "ur" or "hi" or "pa"
            ? replyLanguage   // romanized South Asian text; Pick() maps it to an Indian-English voice
            : LanguageForScript(script, replyLanguage) ?? replyLanguage;
        return new SpeechLine(step.Say, code);
    }

    /// <summary>Picks the best voice for a line, or null to let the system default speak.</summary>
    public static VoiceInfo? Pick(SpeechLine line, IReadOnlyList<VoiceInfo> voices, string? preferredVoiceName = null)
    {
        if (voices.Count == 0) return null;
        var script = ScriptDetector.Detect(line.Text);
        var lang = line.LanguageCode?.ToLowerInvariant();

        // Text in Latin letters must go to a Latin-reading voice, even if the "language" is Urdu/Hindi.
        if (script == TextScript.Latin)
        {
            var romanSouthAsian = lang is "ur" or "hi" or "pa";
            if (!romanSouthAsian && lang is not null && lang != "en")
            {
                var native = voices.FirstOrDefault(v => v.LanguageCode == lang);
                if (native is not null) return native;
            }

            var preferred = Find(voices, preferredVoiceName);
            if (preferred is not null && preferred.LanguageCode == "en") return preferred;

            // An Indian-English voice pronounces Roman Urdu / Hinglish far better than a US one.
            if (romanSouthAsian)
                return FirstTag(voices, "en-IN") ?? FirstTag(voices, "en-GB") ?? FirstLang(voices, "en");
            return FirstLang(voices, "en");
        }

        var wanted = lang ?? LanguageForScript(script, null);
        if (script == TextScript.Arabic && wanted is not ("ur" or "ar" or "fa" or "ps" or "sd"))
            wanted = "ur";
        if (script == TextScript.Devanagari && wanted is not ("hi" or "mr" or "ne"))
            wanted = "hi";

        var pref = Find(voices, preferredVoiceName);
        if (pref is not null && pref.LanguageCode == wanted) return pref;
        var match = wanted is null ? null : FirstLang(voices, wanted);

        // Hindi and Urdu voices can read each other's speech reasonably only in their own script,
        // so with no matching voice we return null and the caller shows the caption only.
        return match;
    }

    private static string? LanguageForScript(TextScript script, string? fallback) => script switch
    {
        TextScript.Arabic => fallback is "ar" or "fa" or "ps" or "sd" ? fallback : "ur",
        TextScript.Devanagari => fallback is "mr" or "ne" ? fallback : "hi",
        TextScript.Latin => fallback is null or "ur" or "hi" or "pa" ? "en" : fallback,
        _ => fallback,
    };

    private static VoiceInfo? Find(IReadOnlyList<VoiceInfo> v, string? name) =>
        string.IsNullOrWhiteSpace(name) ? null : v.FirstOrDefault(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    private static VoiceInfo? FirstTag(IReadOnlyList<VoiceInfo> v, string tag) =>
        v.FirstOrDefault(x => x.LanguageTag.Equals(tag, StringComparison.OrdinalIgnoreCase));

    private static VoiceInfo? FirstLang(IReadOnlyList<VoiceInfo> v, string code) =>
        v.FirstOrDefault(x => x.LanguageCode == code);
}
