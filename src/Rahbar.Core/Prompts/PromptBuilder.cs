using System.Reflection;
using System.Text;
using Rahbar.Core.Abstractions;
using Rahbar.Core.Language;

namespace Rahbar.Core.Prompts;

public sealed record ScreenContext(string WindowTitle, string ProcessName, int ImageWidth, int ImageHeight,
    int? CursorX = null, int? CursorY = null);

public static class PromptBuilder
{
    /// <summary>Spoken question used when the hotkey was only tapped (no speech).</summary>
    public const string ExplainScreenQuestion = "(The user tapped the key without speaking.) Briefly explain what I'm looking at on this screen and what I can do here.";

    private static readonly Lazy<string> Template = new(() =>
    {
        using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("Rahbar.Core.Prompts.brain_system.txt")
                      ?? throw new InvalidOperationException("brain_system.txt resource missing");
        using var r = new StreamReader(s);
        return r.ReadToEnd();
    });

    public static string BuildSystemPrompt(ScreenContext ctx, LanguageProfile lang, string? extraInstructions = null)
    {
        var sb = new StringBuilder(Template.Value)
            .Replace("{window_title}", Clean(ctx.WindowTitle, "unknown"))
            .Replace("{process_name}", Clean(ctx.ProcessName, "unknown"))
            .Replace("{w}", ctx.ImageWidth.ToString())
            .Replace("{h}", ctx.ImageHeight.ToString())
            .Replace("{language_rule}", lang.ReplyRule);
        if (!string.IsNullOrWhiteSpace(extraInstructions))
            sb.AppendLine().AppendLine("EXTRA INSTRUCTIONS:").AppendLine(extraInstructions.Trim());
        return sb.ToString();
    }

    public static string BuildUserText(string question, ScreenContext ctx, IReadOnlyList<ConversationTurn> recent)
    {
        var sb = new StringBuilder();
        if (recent.Count > 0)
        {
            sb.AppendLine("Recent conversation (for context only):");
            foreach (var t in recent)
                sb.Append("User: ").AppendLine(t.Question).Append("Rahbar: ").AppendLine(t.Answer);
            sb.AppendLine();
        }
        if (ctx.CursorX is int cx && ctx.CursorY is int cy)
            sb.AppendLine($"The mouse cursor is at about ({cx}, {cy}) in the screenshot.");
        sb.Append("User's question: ").Append(string.IsNullOrWhiteSpace(question) ? ExplainScreenQuestion : question.Trim());
        return sb.ToString();
    }

    private static string Clean(string? s, string fallback)
    {
        if (string.IsNullOrWhiteSpace(s)) return fallback;
        s = s.Replace('\n', ' ').Replace('\r', ' ').Trim();
        return s.Length > 120 ? s[..120] + "…" : s;
    }
}
