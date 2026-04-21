namespace ServantClaw.Telegram.Transport;

public interface ITelegramPollingClient
{
    ValueTask DropPendingUpdatesAsync(CancellationToken cancellationToken);

    ValueTask<IReadOnlyList<TelegramIncomingUpdate>> GetUpdatesAsync(
        int? offset,
        TimeSpan timeout,
        CancellationToken cancellationToken);
}
