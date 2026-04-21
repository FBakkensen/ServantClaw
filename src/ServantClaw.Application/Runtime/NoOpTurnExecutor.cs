using Microsoft.Extensions.Logging;

namespace ServantClaw.Application.Runtime;

public sealed partial class NoOpTurnExecutor(ILogger<NoOpTurnExecutor> logger) : ITurnExecutor
{
    private readonly ILogger<NoOpTurnExecutor> logger = logger ?? throw new ArgumentNullException(nameof(logger));

    public ValueTask ExecuteAsync(QueuedTurn turn, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(turn);

        Log.NoExecutorWired(
            logger,
            turn.Context.ChatId.Value,
            turn.Context.Agent.ToString(),
            turn.Context.ProjectId.Value);

        return ValueTask.CompletedTask;
    }

    private static partial class Log
    {
        [LoggerMessage(
            EventId = 401,
            Level = LogLevel.Warning,
            Message = "No turn executor wired yet; dropping queued turn for chat {ChatId} agent {Agent} project {ProjectId}")]
        public static partial void NoExecutorWired(
            ILogger logger,
            long chatId,
            string agent,
            string projectId);
    }
}
