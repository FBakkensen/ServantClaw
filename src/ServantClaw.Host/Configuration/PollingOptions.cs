using System.ComponentModel.DataAnnotations;
using ServantClaw.Domain.Configuration;

namespace ServantClaw.Host.Configuration;

public sealed class PollingOptions
{
    [Required]
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);

    [Required]
    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromSeconds(5);

    public PollingConfiguration ToDomainConfiguration() =>
        new(Timeout, RetryDelay);
}
