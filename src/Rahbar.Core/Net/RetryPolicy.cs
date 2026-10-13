using System.Net;
using Rahbar.Core.Abstractions;

namespace Rahbar.Core.Net;

/// <summary>Retries HTTP calls on 429 / 5xx with exponential backoff, honouring Retry-After.</summary>
public sealed class RetryPolicy
{
    public int MaxAttempts { get; init; } = 3;
    public TimeSpan BaseDelay { get; init; } = TimeSpan.FromSeconds(1);

    /// <summary>Longest we will wait between attempts; a longer Retry-After means "give up and tell the user".</summary>
    public TimeSpan MaxDelay { get; init; } = TimeSpan.FromSeconds(8);

    public Func<TimeSpan, CancellationToken, Task> Delay { get; init; } = Task.Delay;

    public static RetryPolicy Default { get; } = new();

    public async Task<HttpResponseMessage> SendAsync(string providerName, Func<HttpRequestMessage> makeRequest,
        HttpClient http, CancellationToken ct)
    {
        for (int attempt = 1; ; attempt++)
        {
            HttpResponseMessage resp;
            try
            {
                resp = await http.SendAsync(makeRequest(), ct).ConfigureAwait(false);
            }
            catch (HttpRequestException ex) when (attempt < MaxAttempts && !ct.IsCancellationRequested)
            {
                _ = ex;
                await Delay(Backoff(attempt), ct).ConfigureAwait(false);
                continue;
            }
            catch (HttpRequestException ex)
            {
                throw new FriendlyException($"{providerName}: can't connect. Check the internet connection.", ex);
            }
            catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
            {
                throw new FriendlyException($"{providerName}: took too long to answer. Please try again.", ex);
            }

            if (resp.IsSuccessStatusCode) return resp;

            var status = (int)resp.StatusCode;
            if (resp.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                resp.Dispose();
                throw new ProviderAuthException(providerName);
            }

            var retryable = resp.StatusCode == HttpStatusCode.TooManyRequests || status >= 500;
            if (!retryable) return resp; // caller reads the error body

            var retryAfter = GetRetryAfter(resp);
            var wait = retryAfter ?? Backoff(attempt);
            if (attempt >= MaxAttempts || wait > MaxDelay)
            {
                resp.Dispose();
                if (resp.StatusCode == HttpStatusCode.TooManyRequests) throw new RateLimitException(providerName, retryAfter);
                throw new FriendlyException($"{providerName} is busy right now (error {status}). Try again in a moment.");
            }

            resp.Dispose();
            await Delay(wait, ct).ConfigureAwait(false);
        }
    }

    internal TimeSpan Backoff(int attempt) =>
        TimeSpan.FromMilliseconds(BaseDelay.TotalMilliseconds * Math.Pow(2, attempt - 1));

    private static TimeSpan? GetRetryAfter(HttpResponseMessage resp)
    {
        var ra = resp.Headers.RetryAfter;
        if (ra?.Delta is TimeSpan d) return d;
        if (ra?.Date is DateTimeOffset when) return when - DateTimeOffset.UtcNow;
        return null;
    }
}
