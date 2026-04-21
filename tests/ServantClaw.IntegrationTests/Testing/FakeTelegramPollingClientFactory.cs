using System.Collections.Concurrent;
using ServantClaw.Telegram.Transport;

namespace ServantClaw.IntegrationTests.Testing;

internal sealed class FakeTelegramPollingClientFactory : ITelegramPollingClientFactory
{
    private readonly FakeTelegramPollingClient sharedClient;

    public FakeTelegramPollingClientFactory(FakeTelegramPollingClient? sharedClient = null)
    {
        this.sharedClient = sharedClient ?? new FakeTelegramPollingClient();
    }

    public FakeTelegramPollingClient SharedClient => sharedClient;

    public ITelegramPollingClient Create(string botToken) => sharedClient;
}

internal sealed class FakeTelegramPollingClient : ITelegramPollingClient
{
    private readonly ConcurrentQueue<IReadOnlyList<TelegramIncomingUpdate>> pendingBatches = new();

    public int DropPendingUpdatesCalls { get; private set; }

    public void EnqueueBatch(params TelegramIncomingUpdate[] updates) =>
        pendingBatches.Enqueue(updates);

    public ValueTask DropPendingUpdatesAsync(CancellationToken cancellationToken)
    {
        DropPendingUpdatesCalls++;
        return ValueTask.CompletedTask;
    }

    public async ValueTask<IReadOnlyList<TelegramIncomingUpdate>> GetUpdatesAsync(
        int? offset,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        if (pendingBatches.TryDequeue(out IReadOnlyList<TelegramIncomingUpdate>? updates))
        {
            return updates;
        }

        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        return [];
    }
}
