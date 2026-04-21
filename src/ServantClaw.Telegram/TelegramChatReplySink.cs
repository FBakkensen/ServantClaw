using ServantClaw.Application.Commands;
using ServantClaw.Domain.Common;
using ServantClaw.Domain.Configuration;
using ServantClaw.Telegram.Transport;

namespace ServantClaw.Telegram;

public sealed class TelegramChatReplySink : IChatReplySink
{
    private readonly ITelegramPollingClient pollingClient;

    public TelegramChatReplySink(
        TelegramConfiguration telegramConfiguration,
        ITelegramPollingClientFactory pollingClientFactory)
    {
        ArgumentNullException.ThrowIfNull(telegramConfiguration);
        ArgumentNullException.ThrowIfNull(pollingClientFactory);

        pollingClient = pollingClientFactory.Create(telegramConfiguration.BotToken);
    }

    public ValueTask SendMessageAsync(ChatId chatId, string message, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        return pollingClient.SendMessageAsync(chatId.Value, message.Trim(), cancellationToken);
    }
}
