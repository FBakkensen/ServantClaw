using System.Diagnostics.CodeAnalysis;
using ServantClaw.Domain.Common;

namespace ServantClaw.Domain.Configuration;

[ExcludeFromCodeCoverage]
public sealed record OwnerConfiguration(UserId UserId, string? Username = null)
{
    public string? Username { get; } = string.IsNullOrWhiteSpace(Username) ? null : Username.Trim();
}
