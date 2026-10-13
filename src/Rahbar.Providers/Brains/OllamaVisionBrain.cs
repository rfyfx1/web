using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Rahbar.Core.Abstractions;
using Rahbar.Core.Net;

namespace Rahbar.Providers.Brains;

/// <summary>Local model through Ollama. Free and private: nothing leaves the PC.</summary>
public sealed class OllamaVisionBrain : IVisionBrain, ITestableProvider
{
    private readonly HttpClient _http;
    private readonly string _baseUrl;
    private readonly string _model;

    public OllamaVisionBrain(HttpClient http, string baseUrl, string model)
    {
        _http = http;
        _baseUrl = baseUrl.TrimEnd('/');
        _model = model.Trim();
    }

    public string Name => "Ollama (local)";

    public async Task<BrainReply> AskAsync(BrainRequest req, CancellationToken ct)
    {
        var body = new JsonObject
        {
            ["model"] = _model,
            ["stream"] = false,
            ["format"] = "json",
            ["options"] = new JsonObject { ["temperature"] = 0.2 },
            ["messages"] = new JsonArray(
                new JsonObject { ["role"] = "system", ["content"] = req.SystemPrompt },
                new JsonObject
                {
                    ["role"] = "user",
                    ["content"] = req.UserText,
                    ["images"] = new JsonArray(Convert.ToBase64String(req.Image)),
                }),
        };
        var json = body.ToJsonString();

        using var resp = await Send(() => new HttpRequestMessage(HttpMethod.Post, _baseUrl + "/api/chat")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        }, ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode) throw await HttpErrors.ToExceptionAsync(Name, resp, ct).ConfigureAwait(false);

        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false));
        var text = doc.RootElement.TryGetProperty("message", out var msg) && msg.TryGetProperty("content", out var c)
            ? c.GetString() ?? "" : "";
        return new BrainReply(text);
    }

    public async Task<string> TestAsync(CancellationToken ct)
    {
        using var resp = await Send(() => new HttpRequestMessage(HttpMethod.Get, _baseUrl + "/api/tags"), ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode) throw await HttpErrors.ToExceptionAsync(Name, resp, ct).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false));
        var names = doc.RootElement.GetProperty("models").EnumerateArray()
            .Select(m => m.GetProperty("name").GetString() ?? "").ToList();
        var wanted = _model.Contains(':') ? _model : _model + ":latest";
        if (!names.Contains(wanted, StringComparer.OrdinalIgnoreCase))
            throw new FriendlyException($"Ollama is running but '{_model}' isn't downloaded. Run: ollama pull {_model}");
        return $"OK – {_model} is installed.";
    }

    private async Task<HttpResponseMessage> Send(Func<HttpRequestMessage> make, CancellationToken ct)
    {
        try
        {
            return await RetryPolicy.Default.SendAsync(Name, make, _http, ct).ConfigureAwait(false);
        }
        catch (FriendlyException ex) when (ex.InnerException is HttpRequestException)
        {
            throw new FriendlyException("Ollama isn't running. Install it from ollama.com and start it, or turn off Private mode.", ex);
        }
    }
}
