using System.Diagnostics.CodeAnalysis;
using ServantClaw.Domain.Routing;

namespace ServantClaw.Application.Runtime;

[ExcludeFromCodeCoverage]
public sealed record QueuedTurn(ThreadContext Context, string MessageText, DateTimeOffset EnqueuedAtUtc)
{
    public ThreadContext Context { get; } = Context ?? throw new ArgumentNullException(nameof(Context));

    public string MessageText { get; } = string.IsNullOrWhiteSpace(MessageText)
        ? throw new ArgumentException("Turn message text must be provided.", nameof(MessageText))
        : MessageText.Trim();
}
