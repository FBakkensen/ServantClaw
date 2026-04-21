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

internal sealed class FakeTelegramPollingClient : ITelegramPollingClient, IDisposable
{
    private readonly ConcurrentQueue<IReadOnlyList<TelegramIncomingUpdate>> pendingBatches = new();
    private readonly SemaphoreSlim pendingBatchSignal = new(0);
    private readonly ConcurrentQueue<SentTelegramMessage> sentMessages = new();
    private readonly SemaphoreSlim sentMessageSignal = new(0);

    public int DropPendingUpdatesCalls { get; private set; }

    public void EnqueueBatch(params TelegramIncomingUpdate[] updates)
    {
        pendingBatches.Enqueue(updates);
        pendingBatchSignal.Release();
    }

    public ValueTask DropPendingUpdatesAsync(CancellationToken cancellationToken)
    {
        DropPendingUpdatesCalls++;
        return ValueTask.CompletedTask;
    }

    public ValueTask SendMessageAsync(long chatId, string text, CancellationToken cancellationToken)
    {
        sentMessages.Enqueue(new SentTelegramMessage(chatId, text));
        sentMessageSignal.Release();
        return ValueTask.CompletedTask;
    }

    public async ValueTask<IReadOnlyList<TelegramIncomingUpdate>> GetUpdatesAsync(
        int? offset,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        await pendingBatchSignal.WaitAsync(cancellationToken);
        return pendingBatches.TryDequeue(out IReadOnlyList<TelegramIncomingUpdate>? updates)
            ? updates
            : [];
    }

    public async Task<SentTelegramMessage> DequeueSentMessageAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        using CancellationTokenSource timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);

        await sentMessageSignal.WaitAsync(timeoutSource.Token);
        return sentMessages.TryDequeue(out SentTelegramMessage? message)
            ? message
            : throw new InvalidOperationException("Sent message signal was raised without a queued message.");
    }

    public void Dispose()
    {
        pendingBatchSignal.Dispose();
        sentMessageSignal.Dispose();
    }
}

internal sealed record SentTelegramMessage(long ChatId, string Text);
