using System.Diagnostics.CodeAnalysis;

namespace ServantClaw.Domain.Configuration;

[ExcludeFromCodeCoverage]
public sealed record PollingConfiguration
{
    public PollingConfiguration(TimeSpan timeout, TimeSpan retryDelay)
    {
        if (timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout), "Polling timeout must be positive.");
        }

        if (retryDelay < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(retryDelay), "Polling retry delay cannot be negative.");
        }

        Timeout = timeout;
        RetryDelay = retryDelay;
    }

    public TimeSpan Timeout { get; }

    public TimeSpan RetryDelay { get; }
}
