using System.Net.Http.Headers;
using System.Text.Json;
using Rahbar.Core.Abstractions;
using Rahbar.Core.Net;
using Rahbar.Providers.Brains;

namespace Rahbar.Providers.Speech;

/// <summary>Groq-hosted Whisper (OpenAI-compatible /audio/transcriptions). Handles Urdu, Hindi and mixed speech.</summary>
public sealed class GroqSpeechToText : ISpeechToText, ITestableProvider
{
    private const string BaseUrl = "https://api.groq.com/openai/v1/";
    private readonly HttpClient _http;
    private readonly string _apiKey;
    private readonly string _model;

    public GroqSpeechToText(HttpClient http, string apiKey, string model)
    {
        _http = http;
        _apiKey = apiKey;
        _model = model.Trim();
    }

    public string Name => "Groq Whisper";

    public async Task<string> TranscribeAsync(byte[] wav, string? languageHint, string? stylePrompt, CancellationToken ct)
    {
        using var resp = await RetryPolicy.Default.SendAsync(Name, () =>
        {
            var form = new MultipartFormDataContent();
            var file = new ByteArrayContent(wav);
            file.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
            form.Add(file, "file", "question.wav");
            form.Add(new StringContent(_model), "model");
            form.Add(new StringContent("json"), "response_format");
            form.Add(new StringContent("0"), "temperature");
            if (!string.IsNullOrWhiteSpace(languageHint)) form.Add(new StringContent(languageHint), "language");
            if (!string.IsNullOrWhiteSpace(stylePrompt)) form.Add(new StringContent(stylePrompt), "prompt");

            var m = new HttpRequestMessage(HttpMethod.Post, BaseUrl + "audio/transcriptions") { Content = form };
            m.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
            return m;
        }, _http, ct).ConfigureAwait(false);

        if (!resp.IsSuccessStatusCode) throw await HttpErrors.ToExceptionAsync(Name, resp, ct).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false));
        return doc.RootElement.TryGetProperty("text", out var t) ? t.GetString() ?? "" : "";
    }

    public async Task<string> TestAsync(CancellationToken ct)
    {
        using var resp = await RetryPolicy.Default.SendAsync(Name, () =>
        {
            var m = new HttpRequestMessage(HttpMethod.Get, BaseUrl + "models");
            m.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
            return m;
        }, _http, ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode) throw await HttpErrors.ToExceptionAsync(Name, resp, ct).ConfigureAwait(false);

        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false));
        var ids = doc.RootElement.GetProperty("data").EnumerateArray().Select(m => m.GetProperty("id").GetString()).ToList();
        if (!ids.Contains(_model))
            throw new FriendlyException($"Groq key works, but model '{_model}' isn't available. Available Whisper models: " +
                                        string.Join(", ", ids.Where(i => i?.Contains("whisper") == true)));
        return $"OK – {_model} is ready.";
    }
}
