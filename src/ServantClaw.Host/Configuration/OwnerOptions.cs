using ServantClaw.Domain.Common;
using ServantClaw.Domain.Configuration;

namespace ServantClaw.Host.Configuration;

public sealed class OwnerOptions
{
    public const string SectionName = "Owner";

    public long UserId { get; set; }

    public string? Username { get; set; }

    public OwnerConfiguration ToDomainConfiguration() =>
        new(new UserId(UserId), Username);
}
