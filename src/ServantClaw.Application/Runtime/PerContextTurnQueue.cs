using System.Diagnostics.CodeAnalysis;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using ServantClaw.Domain.Routing;

namespace ServantClaw.Application.Runtime;

[SuppressMessage(
    "Naming",
    "CA1711:Identifiers should not have incorrect suffix",
    Justification = "Queue is the design-level term for the per-context turn ordering primitive.")]
public sealed partial class PerContextTurnQueue(
    ITurnExecutor executor,
    ILogger<PerContextTurnQueue> logger) : IPerContextTurnQueue, IHostRuntimeParticipant, IAsyncDisposable
{
    private readonly ITurnExecutor executor = executor ?? throw new ArgumentNullException(nameof(executor));
    private readonly ILogger<PerContextTurnQueue> logger = logger ?? throw new ArgumentNullException(nameof(logger));
    private readonly Dictionary<ThreadContext, ContextWorker> workers = [];
    private readonly Lock workersGate = new();

    private CancellationTokenSource? shutdownSource;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        lock (workersGate)
        {
            if (shutdownSource is not null)
            {
                throw new InvalidOperationException("Queue has already been started.");
            }

            shutdownSource = new CancellationTokenSource();
        }

        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        CancellationTokenSource? source;
        ContextWorker[] workersToStop;

        lock (workersGate)
        {
            source = shutdownSource;
            shutdownSource = null;
            workersToStop = [.. workers.Values];
            workers.Clear();
        }

        if (source is null)
        {
            return;
        }

        await source.CancelAsync();
        foreach (ContextWorker worker in workersToStop)
        {
            // Stryker disable once all : equivalent - cancellation already drives worker exit; TryComplete is belt-and-suspenders so the reader sees clean completion.
            worker.Channel.Writer.TryComplete();
        }

        try
        {
            await Task.WhenAll(workersToStop.Select(worker => worker.RunningTask)).WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
        }

        // Stryker disable once Statement : low-value - disposing the cancellation source is resource hygiene, not observable behavior.
        source.Dispose();
    }

    public ValueTask EnqueueAsync(QueuedTurn turn, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(turn);

        ContextWorker worker;
        lock (workersGate)
        {
            if (shutdownSource is null)
            {
                throw new InvalidOperationException("Queue has not been started or has already been stopped.");
            }

            if (!workers.TryGetValue(turn.Context, out ContextWorker? existingWorker))
            {
                existingWorker = CreateWorker(turn.Context, shutdownSource.Token);
                workers[turn.Context] = existingWorker;
            }

            worker = existingWorker;
        }

        return worker.Channel.Writer.WriteAsync(turn, cancellationToken);
    }

    private ContextWorker CreateWorker(ThreadContext context, CancellationToken shutdownToken)
    {
        Channel<QueuedTurn> channel = Channel.CreateUnbounded<QueuedTurn>(
            // Stryker disable once all : equivalent - SingleReader=true is a Channels perf hint for our one-worker-per-context layout; an unbounded channel with multi-reader-safe implementation is functionally identical here.
            new UnboundedChannelOptions { SingleReader = true });
        Task runningTask = Task.Run(() => RunWorkerAsync(context, channel.Reader, shutdownToken), shutdownToken);
        return new ContextWorker(channel, runningTask);
    }

    private async Task RunWorkerAsync(
        ThreadContext context,
        ChannelReader<QueuedTurn> reader,
        CancellationToken shutdownToken)
    {
        try
        {
            await foreach (QueuedTurn turn in reader.ReadAllAsync(shutdownToken))
            {
                try
                {
                    await executor.ExecuteAsync(turn, shutdownToken);
                }
                catch (Exception exception)
                {
                    Log.TurnExecutionFailed(
                        logger,
                        context.ChatId.Value,
                        context.Agent.ToString(),
                        context.ProjectId.Value,
                        exception);
                }
            }
        }
        catch (OperationCanceledException) when (shutdownToken.IsCancellationRequested)
        {
        }
    }

    public ValueTask DisposeAsync() => new(StopAsync(CancellationToken.None));

    private sealed record ContextWorker(Channel<QueuedTurn> Channel, Task RunningTask);

    private static partial class Log
    {
        [LoggerMessage(
            EventId = 400,
            Level = LogLevel.Error,
            Message = "Turn execution failed for chat {ChatId} agent {Agent} project {ProjectId}; continuing with next queued turn")]
        public static partial void TurnExecutionFailed(
            ILogger logger,
            long chatId,
            string agent,
            string projectId,
            Exception exception);
    }
}
