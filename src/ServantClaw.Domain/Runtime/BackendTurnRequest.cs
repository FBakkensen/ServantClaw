using System.Diagnostics.CodeAnalysis;
using ServantClaw.Domain.Routing;

namespace ServantClaw.Domain.Runtime;

[ExcludeFromCodeCoverage]
public sealed record BackendTurnRequest(ThreadContext Context, string Message)
{
    public string Message { get; } = string.IsNullOrWhiteSpace(Message)
        ? throw new ArgumentException("Turn message cannot be empty.", nameof(Message))
        : Message.Trim();
}
