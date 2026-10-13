using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Rahbar.Core.Abstractions;
using Rahbar.Core.Net;

namespace Rahbar.Providers.Brains;

/// <summary>Anthropic Claude via the Messages API. Best pointing accuracy; paid.</summary>
public sealed class ClaudeVisionBrain : IVisionBrain, ITestableProvider
{
    private const string BaseUrl = "https://api.anthropic.com/v1/";
    private readonly HttpClient _http;
    private readonly string _apiKey;
    private readonly string _model;

    public ClaudeVisionBrain(HttpClient http, string apiKey, string model)
    {
        _http = http;
        _apiKey = apiKey;
        _model = model.Trim();
    }

    public string Name => "Claude";

    public async Task<BrainReply> AskAsync(BrainRequest req, CancellationToken ct)
    {
        var body = new JsonObject
        {
            ["model"] = _model,
            ["max_tokens"] = 1500,
            ["system"] = req.SystemPrompt,
            ["messages"] = new JsonArray(new JsonObject
            {
                ["role"] = "user",
                ["content"] = new JsonArray(
                    new JsonObject
                    {
                        ["type"] = "image",
                        ["source"] = new JsonObject
                        {
                            ["type"] = "base64",
                            ["media_type"] = req.ImageMimeType,
                            ["data"] = Convert.ToBase64String(req.Image),
                        },
                    },
                    new JsonObject { ["type"] = "text", ["text"] = req.UserText }),
            }),
        };
        var json = body.ToJsonString();

        using var resp = await RetryPolicy.Default.SendAsync(Name, () => Request(HttpMethod.Post, "messages", json), _http, ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode) throw await HttpErrors.ToExceptionAsync(Name, resp, ct).ConfigureAwait(false);

        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false));
        var sb = new StringBuilder();
        if (doc.RootElement.TryGetProperty("content", out var content))
            foreach (var block in content.EnumerateArray())
                if (block.TryGetProperty("type", out var t) && t.GetString() == "text")
                    sb.Append(block.GetProperty("text").GetString());
        return new BrainReply(sb.ToString());
    }

    public async Task<string> TestAsync(CancellationToken ct)
    {
        using var resp = await RetryPolicy.Default.SendAsync(Name, () => Request(HttpMethod.Get, "models/" + Uri.EscapeDataString(_model), null), _http, ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode) throw await HttpErrors.ToExceptionAsync(Name, resp, ct).ConfigureAwait(false);
        return $"OK – {_model} is ready.";
    }

    private HttpRequestMessage Request(HttpMethod method, string path, string? json)
    {
        var m = new HttpRequestMessage(method, BaseUrl + path);
        m.Headers.Add("x-api-key", _apiKey);
        m.Headers.Add("anthropic-version", "2023-06-01");
        if (json is not null) m.Content = new StringContent(json, Encoding.UTF8, "application/json");
        return m;
    }
}
