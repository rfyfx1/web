namespace Rahbar.Core.Abstractions;

/// <summary>An error that already carries a friendly, non-technical message for the team.</summary>
public class FriendlyException : Exception
{
    public FriendlyException(string friendlyMessage, Exception? inner = null) : base(friendlyMessage, inner) { }
}

public sealed class RateLimitException : FriendlyException
{
    public TimeSpan? RetryAfter { get; }
    public RateLimitException(string provider, TimeSpan? retryAfter)
        : base($"{provider}: free limit reached, try again in a minute.") => RetryAfter = retryAfter;
}

public sealed class ProviderAuthException : FriendlyException
{
    public ProviderAuthException(string provider)
        : base($"{provider}: the API key was rejected. Open Settings and paste the key again.") { }
}
