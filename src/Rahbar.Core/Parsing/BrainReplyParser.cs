using System.Globalization;
using System.Text.Json;
using Rahbar.Core.Abstractions;

namespace Rahbar.Core.Parsing;

/// <summary>
/// Turns the model's text into a <see cref="BrainAnswer"/>. Models sometimes wrap JSON in
/// ```fences```, add a sentence before it, or send numbers as strings, so this is forgiving.
/// </summary>
public static class BrainReplyParser
{
    public const int MaxSteps = 6;

    private static readonly JsonDocumentOptions Options = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip,
    };

    public static bool TryParse(string? raw, int imageWidth, int imageHeight, out BrainAnswer answer, out string? error)
    {
        answer = new BrainAnswer();
        error = null;
        var json = ExtractJsonObject(raw);
        if (json is null) { error = "No JSON object found in reply."; return false; }

        try
        {
            using var doc = JsonDocument.Parse(json, Options);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) { error = "Reply JSON is not an object."; return false; }

            if (TryGet(root, "steps", out var steps) && steps.ValueKind == JsonValueKind.Array)
            {
                foreach (var s in steps.EnumerateArray())
                {
                    var step = ParseStep(s, imageWidth, imageHeight);
                    if (step is not null) answer.Steps.Add(step);
                    if (answer.Steps.Count == MaxSteps) break;
                }
            }
            else
            {
                // Lenient: a single step written at the top level.
                var step = ParseStep(root, imageWidth, imageHeight);
                if (step is not null) answer.Steps.Add(step);
            }

            if (answer.Steps.Count == 0) { error = "Reply had no steps with text."; return false; }

            answer.Language = (GetString(root, "language") ?? "en").Trim().ToLowerInvariant();
            if (answer.Language.Length > 3) answer.Language = answer.Language.Split('-', '|', '_')[0];
            answer.NeedsMoreSteps = TryGet(root, "needs_more_steps", out var more) && more.ValueKind == JsonValueKind.True;
            return true;
        }
        catch (JsonException ex)
        {
            error = "Invalid JSON: " + ex.Message;
            return false;
        }
    }

    /// <summary>Returns the text from the first '{' to its matching '}', ignoring braces inside strings.</summary>
    internal static string? ExtractJsonObject(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        int start = raw.IndexOf('{');
        if (start < 0) return null;

        int depth = 0;
        bool inString = false, escaped = false;
        for (int i = start; i < raw.Length; i++)
        {
            char c = raw[i];
            if (inString)
            {
                if (escaped) escaped = false;
                else if (c == '\\') escaped = true;
                else if (c == '"') inString = false;
                continue;
            }
            if (c == '"') inString = true;
            else if (c == '{') depth++;
            else if (c == '}' && --depth == 0) return raw.Substring(start, i - start + 1);
        }
        return null;
    }

    private static BrainStep? ParseStep(JsonElement s, int w, int h)
    {
        if (s.ValueKind != JsonValueKind.Object) return null;
        var say = GetString(s, "say") ?? GetString(s, "text") ?? GetString(s, "answer");
        if (string.IsNullOrWhiteSpace(say)) return null;

        var step = new BrainStep
        {
            Say = say.Trim(),
            Speak = NullIfBlank(GetString(s, "speak")),
            SpeakLanguage = NullIfBlank(GetString(s, "speak_language"))?.ToLowerInvariant(),
        };
        if (TryGet(s, "point", out var p) && p.ValueKind == JsonValueKind.Object)
            step.Point = ParsePoint(p, w, h);
        return step;
    }

    private static PointTarget? ParsePoint(JsonElement p, int w, int h)
    {
        var x = GetNumber(p, "x");
        var y = GetNumber(p, "y");
        if (x is null || y is null) return null;

        // Models occasionally answer in 0..1 fractions; scale those up.
        if (x <= 1 && y <= 1 && x >= 0 && y >= 0 && (x > 0 || y > 0) && w > 2 && h > 2)
        {
            x *= w; y *= h;
        }

        // Reject points that are clearly off the screenshot; clamp ones that are just over the edge.
        const double slack = 8;
        if (w > 0 && h > 0 && (x < -slack || y < -slack || x > w + slack || y > h + slack)) return null;
        if (w > 0) x = Math.Clamp(x.Value, 0, w - 1);
        if (h > 0) y = Math.Clamp(y.Value, 0, h - 1);

        var type = (GetString(p, "type") ?? "circle").Trim().ToLowerInvariant() switch
        {
            "arrow" => PointType.Arrow,
            "box" or "rect" or "rectangle" or "highlight" => PointType.Box,
            _ => PointType.Circle,
        };
        return new PointTarget
        {
            X = x.Value,
            Y = y.Value,
            Type = type,
            W = Math.Max(0, GetNumber(p, "w") ?? 0),
            H = Math.Max(0, GetNumber(p, "h") ?? 0),
            Label = NullIfBlank(GetString(p, "label")),
        };
    }

    private static bool TryGet(JsonElement e, string name, out JsonElement value)
    {
        foreach (var prop in e.EnumerateObject())
            if (prop.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) { value = prop.Value; return true; }
        value = default;
        return false;
    }

    private static string? GetString(JsonElement e, string name) =>
        TryGet(e, name, out var v) ? v.ValueKind switch
        {
            JsonValueKind.String => v.GetString(),
            JsonValueKind.Number => v.GetRawText(),
            _ => null,
        } : null;

    private static double? GetNumber(JsonElement e, string name)
    {
        if (!TryGet(e, name, out var v)) return null;
        if (v.ValueKind == JsonValueKind.Number) return v.GetDouble();
        if (v.ValueKind == JsonValueKind.String &&
            double.TryParse(v.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d)) return d;
        return null;
    }

    private static string? NullIfBlank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
