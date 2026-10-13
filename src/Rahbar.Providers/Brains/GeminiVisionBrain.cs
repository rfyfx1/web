using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Rahbar.Core.Abstractions;
using Rahbar.Core.Net;

namespace Rahbar.Providers.Brains;

/// <summary>Google Gemini (default free-mode brain). REST: models/{model}:generateContent.</summary>
public sealed class GeminiVisionBrain : IVisionBrain, ITestableProvider
{
    private const string BaseUrl = "https://generativelanguage.googleapis.com/v1beta/models/";
    private readonly HttpClient _http;
    private readonly string _apiKey;
    private readonly string _model;

    public GeminiVisionBrain(HttpClient http, string apiKey, string model)
    {
        _http = http;
        _apiKey = apiKey;
        _model = model.Trim();
    }

    public string Name => "Gemini";

    public async Task<BrainReply> AskAsync(BrainRequest req, CancellationToken ct)
    {
        var body = new JsonObject
        {
            ["system_instruction"] = new JsonObject { ["parts"] = new JsonArray(new JsonObject { ["text"] = req.SystemPrompt }) },
            ["contents"] = new JsonArray(new JsonObject
            {
                ["role"] = "user",
                ["parts"] = new JsonArray(
                    new JsonObject
                    {
                        ["inline_data"] = new JsonObject
                        {
                            ["mime_type"] = req.ImageMimeType,
                            ["data"] = Convert.ToBase64String(req.Image),
                        },
                    },
                    new JsonObject { ["text"] = req.UserText }),
            }),
            ["generationConfig"] = new JsonObject
            {
                ["responseMimeType"] = "application/json",
                ["temperature"] = 0.2,
                // Headroom for models that "think" before answering.
                ["maxOutputTokens"] = 4096,
            },
        };
        var json = body.ToJsonString();

        using var resp = await RetryPolicy.Default.SendAsync(Name, () =>
        {
            var m = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}{Uri.EscapeDataString(_model)}:generateContent")
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            };
            m.Headers.Add("x-goog-api-key", _apiKey);
            return m;
        }, _http, ct).ConfigureAwait(false);

        if (!resp.IsSuccessStatusCode) throw await HttpErrors.ToExceptionAsync(Name, resp, ct).ConfigureAwait(false);

        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false));
        return new BrainReply(ExtractText(doc.RootElement));
    }

    internal static string ExtractText(JsonElement root)
    {
        var sb = new StringBuilder();
        if (root.TryGetProperty("candidates", out var cands) && cands.GetArrayLength() > 0)
        {
            var c = cands[0];
            if (c.TryGetProperty("content", out var content) && content.TryGetProperty("parts", out var parts))
                foreach (var p in parts.EnumerateArray())
                {
                    if (p.TryGetProperty("thought", out var th) && th.ValueKind == JsonValueKind.True) continue;
                    if (p.TryGetProperty("text", out var t)) sb.Append(t.GetString());
                }
            if (sb.Length == 0 && c.TryGetProperty("finishReason", out var fr) && fr.GetString() is "SAFETY" or "PROHIBITED_CONTENT")
                throw new FriendlyException("Gemini refused to answer about this screen. Try asking differently.");
        }
        if (sb.Length == 0 && root.TryGetProperty("promptFeedback", out var pf) && pf.TryGetProperty("blockReason", out _))
            throw new FriendlyException("Gemini refused to answer about this screen. Try asking differently.");
        return sb.ToString();
    }

    public async Task<string> TestAsync(CancellationToken ct)
    {
        using var resp = await RetryPolicy.Default.SendAsync(Name, () =>
        {
            var m = new HttpRequestMessage(HttpMethod.Get, BaseUrl + Uri.EscapeDataString(_model));
            m.Headers.Add("x-goog-api-key", _apiKey);
            return m;
        }, _http, ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode) throw await HttpErrors.ToExceptionAsync(Name, resp, ct).ConfigureAwait(false);
        var info = await resp.Content.ReadFromJsonAsync<JsonObject>(ct).ConfigureAwait(false);
        return $"OK – {info?["displayName"]?.GetValue<string>() ?? _model} is ready.";
    }
}
