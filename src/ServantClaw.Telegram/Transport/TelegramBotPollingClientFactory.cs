using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace ServantClaw.Telegram.Transport;

public sealed class TelegramBotPollingClientFactory : ITelegramPollingClientFactory
{
    public ITelegramPollingClient Create(string botToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(botToken);
        return new TelegramBotPollingClient(new TelegramBotClient(botToken));
    }

    private sealed class TelegramBotPollingClient(ITelegramBotClient botClient) : ITelegramPollingClient
    {
        public async ValueTask DropPendingUpdatesAsync(CancellationToken cancellationToken) =>
            await botClient.DropPendingUpdates(cancellationToken);

        public async ValueTask SendMessageAsync(long chatId, string text, CancellationToken cancellationToken) =>
            await botClient.SendMessage(chatId, text, cancellationToken: cancellationToken);

        public async ValueTask<IReadOnlyList<TelegramIncomingUpdate>> GetUpdatesAsync(
            int? offset,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            Update[] updates = await botClient.GetUpdates(
                offset: offset,
                timeout: (int)Math.Ceiling(timeout.TotalSeconds),
                allowedUpdates: [UpdateType.Message],
                cancellationToken: cancellationToken);

            return updates.Select(MapUpdate).ToArray();
        }

        private static TelegramIncomingUpdate MapUpdate(Update update) =>
            new(update.Id, MapMessage(update.Message));

        private static TelegramIncomingMessage? MapMessage(Message? message)
        {
            if (message?.From is null)
            {
                return null;
            }

            return new TelegramIncomingMessage(
                message.Chat.Id,
                message.From.Id,
                message.From.Username,
                new DateTimeOffset(message.Date.ToUniversalTime()),
                message.Text);
        }
    }
}
