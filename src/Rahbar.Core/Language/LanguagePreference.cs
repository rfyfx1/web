namespace Rahbar.Core.Language;

/// <summary>
/// How the user talks and how Rahbar should answer. People mix languages freely
/// (English + Urdu, Hinglish, Roman Urdu …), so <see cref="Auto"/> is the default:
/// Rahbar mirrors whatever language, script and mix the user just used.
/// </summary>
public enum LanguagePreference
{
    Auto,
    English,
    Urdu,        // اردو script
    RomanUrdu,   // Urdu written in English letters: "save ka button kahan hai?"
    Hindi,       // देवनागरी script
    Hinglish,    // Hindi + English in English letters: "ye file save kaise karun?"
}

/// <summary>Per-preference settings for speech recognition and the brain's reply rule.</summary>
public sealed record LanguageProfile(
    LanguagePreference Preference,
    string DisplayName,
    string? WhisperLanguage,
    string? WhisperStylePrompt,
    string ReplyRule)
{
    // Whisper writes Urdu/Hindi speech in native script by default. A short sample in the
    // target style, passed as Whisper's "prompt", nudges it toward Roman letters instead.
    private const string RomanUrduSample =
        "Acha, mujhe ye batao ke save ka button kahan hai? Phir next step kya hoga? Okay, theek hai.";
    private const string HinglishSample =
        "Haan bhai, ye file save kaise karun? Next button kahan hai? Okay, thik hai, samajh gaya.";
    private const string MixedSample =
        "Okay, mujhe batao ke ye button kya karta hai? Save kaise karun? Next step kya hai?";

    private const string MirrorRule =
        "Reply in exactly the same language, script and mix the user used. Users may speak any language " +
        "and often mix them: English, Urdu, Roman Urdu (Urdu in English letters), Hindi, Hinglish, Punjabi, " +
        "Arabic or anything else. If they mixed English with Urdu/Hindi, mix the same way. " +
        "If they wrote Urdu/Hindi in English letters, answer in English letters too. " +
        "Keep English names of buttons and menus exactly as they appear on screen.";

    public static LanguageProfile For(LanguagePreference p) => p switch
    {
        LanguagePreference.English => new(p, "English", "en", null,
            "Reply in simple English."),
        LanguagePreference.Urdu => new(p, "اردو (Urdu)", "ur", null,
            "Reply in simple Urdu written in Urdu script. Keep on-screen English button names in English."),
        LanguagePreference.RomanUrdu => new(p, "Roman Urdu", null, RomanUrduSample,
            "Reply in simple Roman Urdu (Urdu written in English letters, e.g. \"Upar left mein File pe click karein\"). " +
            "Keep on-screen English button names in English."),
        LanguagePreference.Hindi => new(p, "हिन्दी (Hindi)", "hi", null,
            "Reply in simple Hindi written in Devanagari. Keep on-screen English button names in English."),
        LanguagePreference.Hinglish => new(p, "Hinglish", null, HinglishSample,
            "Reply in casual Hinglish (Hindi mixed with English, written in English letters, e.g. \"Top left pe File menu kholo\"). " +
            "Keep on-screen English button names in English."),
        _ => new(LanguagePreference.Auto, "Auto (any language)", null, MixedSample, MirrorRule),
    };

    public static IReadOnlyList<LanguageProfile> All { get; } =
        Enum.GetValues<LanguagePreference>().Select(For).ToList();

    /// <summary>True when replies are written in English letters but are really Urdu/Hindi.</summary>
    public bool IsRomanized => Preference is LanguagePreference.RomanUrdu or LanguagePreference.Hinglish;
}
