using System.Diagnostics.CodeAnalysis;

namespace ServantClaw.Application.Runtime;

[SuppressMessage(
    "Naming",
    "CA1711:Identifiers should not have incorrect suffix",
    Justification = "Queue is the design-level term for the per-context turn ordering primitive.")]
public interface IPerContextTurnQueue
{
    ValueTask EnqueueAsync(QueuedTurn turn, CancellationToken cancellationToken);
}
