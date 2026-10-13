using System.Text.Json;
using Rahbar.Core.Abstractions;

namespace Rahbar.Providers.Brains;

internal static class HttpErrors
{
    /// <summary>Reads an error response and turns it into a short message (never includes the key).</summary>
    public static async Task<Exception> ToExceptionAsync(string provider, HttpResponseMessage resp, CancellationToken ct)
    {
        var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        var message = ExtractMessage(body) ?? resp.ReasonPhrase ?? "unknown error";
        if (message.Contains("API key", StringComparison.OrdinalIgnoreCase) &&
            (message.Contains("invalid", StringComparison.OrdinalIgnoreCase) || message.Contains("not valid", StringComparison.OrdinalIgnoreCase)))
            return new ProviderAuthException(provider);
        if (message.Length > 200) message = message[..200] + "…";
        return new FriendlyException($"{provider} error ({(int)resp.StatusCode}): {message}");
    }

    private static string? ExtractMessage(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (root.TryGetProperty("error", out var err))
            {
                if (err.ValueKind == JsonValueKind.String) return err.GetString();
                if (err.TryGetProperty("message", out var m)) return m.GetString();
            }
            if (root.TryGetProperty("message", out var msg)) return msg.GetString();
        }
        catch (JsonException) { }
        return string.IsNullOrWhiteSpace(body) ? null : body.Trim();
    }
}
