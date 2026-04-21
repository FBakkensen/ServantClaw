namespace ServantClaw.Application.Runtime;

public interface ITurnExecutor
{
    ValueTask ExecuteAsync(QueuedTurn turn, CancellationToken cancellationToken);
}
