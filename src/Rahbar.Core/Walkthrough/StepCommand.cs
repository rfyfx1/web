using System.Text;

namespace Rahbar.Core.Walkthrough;

public enum StepCommand { None, Next, Back, Repeat, Stop }

/// <summary>
/// Recognises short spoken commands during a walkthrough, in any language the team uses:
/// "next", "agla", "ho gaya", "اگلا", "अगला", "back", "pichla", "dobara", "bas", …
/// Only short utterances made of command and filler words count, so a real question such as
/// "what is next to the save button?" is still sent to the AI.
/// </summary>
public static class StepCommands
{
    private static readonly HashSet<string> Next = new(StringComparer.OrdinalIgnoreCase)
    {
        "next", "continue", "done", "agla", "agla", "agle", "aagla", "aage", "aagay", "agay", "age", "aglaa",
        "gaya", "gya", "hogaya", "hogya", "liya", "karliya", "proceed", "forward",
        "اگلا", "اگلے", "آگے", "اگے", "گیا", "لیا", "ہوگیا",
        "अगला", "अगले", "आगे", "गया", "लिया", "होगया",
    };

    private static readonly HashSet<string> Back = new(StringComparer.OrdinalIgnoreCase)
    {
        "back", "previous", "prev", "pichla", "pichhla", "pichle", "peeche", "piche", "peechay", "wapas", "wapis",
        "پچھلا", "پیچھے", "واپس",
        "पिछला", "पीछे", "वापस",
    };

    private static readonly HashSet<string> Repeat = new(StringComparer.OrdinalIgnoreCase)
    {
        "repeat", "again", "dobara", "dubara", "dobaara", "phir", "fir", "dohrao",
        "دوبارہ", "پھر", "दोबारा", "फिर",
    };

    private static readonly HashSet<string> Stop = new(StringComparer.OrdinalIgnoreCase)
    {
        "stop", "cancel", "bas", "band", "ruko", "rukk", "ruk", "khatam", "enough", "quit", "exit",
        "بس", "بند", "رکو", "رک", "ختم",
        "बस", "बंद", "रुको", "रुक", "ख़त्म", "खत्म",
    };

    private static readonly HashSet<string> Filler = new(StringComparer.OrdinalIgnoreCase)
    {
        "ok", "okay", "okk", "please", "plz", "pls", "the", "step", "steps", "go", "to", "one", "yes", "yeah", "now", "and",
        "haan", "han", "ha", "ji", "jee", "bhai", "yaar", "yar", "theek", "thik", "hai", "he", "chalo", "karo", "kar", "karein",
        "ho", "se", "batao", "bataiye", "wala", "wali", "acha", "accha", "achha", "ab",
        "ٹھیک", "ہے", "جی", "ہاں", "چلو", "کرو", "کریں", "ہو", "سے", "اب", "اچھا", "بتاؤ", "اسٹیپ",
        "ठीक", "है", "जी", "हाँ", "हां", "चलो", "करो", "करें", "हो", "से", "अब", "अच्छा", "बताओ", "स्टेप",
    };

    public static StepCommand Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return StepCommand.None;
        var words = Tokenise(text);
        if (words.Count == 0 || words.Count > 6) return StepCommand.None;

        bool next = false, back = false, repeat = false, stop = false;
        foreach (var w in words)
        {
            if (Stop.Contains(w)) stop = true;
            else if (Back.Contains(w)) back = true;
            else if (Repeat.Contains(w)) repeat = true;
            else if (Next.Contains(w)) next = true;
            else if (!Filler.Contains(w)) return StepCommand.None; // a real question
        }

        if (stop) return StepCommand.Stop;
        if (back) return StepCommand.Back;
        if (repeat) return StepCommand.Repeat;
        return next ? StepCommand.Next : StepCommand.None;
    }

    private static List<string> Tokenise(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (var ch in text)
            sb.Append(char.IsLetterOrDigit(ch) || char.GetUnicodeCategory(ch) is System.Globalization.UnicodeCategory.NonSpacingMark
                                                 or System.Globalization.UnicodeCategory.SpacingCombiningMark
                ? char.ToLowerInvariant(ch) : ' ');
        return sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
    }
}
