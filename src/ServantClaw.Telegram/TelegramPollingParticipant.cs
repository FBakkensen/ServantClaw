using ServantClaw.Application.Runtime;
using Microsoft.Extensions.Logging;
using ServantClaw.Application.Intake;
using ServantClaw.Application.Intake.Models;
using ServantClaw.Domain.Common;
using ServantClaw.Domain.Configuration;
using ServantClaw.Telegram.Transport;

namespace ServantClaw.Telegram;

public sealed partial class TelegramPollingParticipant(
    TelegramConfiguration telegramConfiguration,
    OwnerConfiguration ownerConfiguration,
    IChatUpdateIntake chatUpdateIntake,
    ITelegramPollingClientFactory pollingClientFactory,
    ILogger<TelegramPollingParticipant> logger) : IHostRuntimeParticipant
{
    private readonly TelegramConfiguration telegramConfiguration =
        telegramConfiguration ?? throw new ArgumentNullException(nameof(telegramConfiguration));
    private readonly OwnerConfiguration ownerConfiguration =
        ownerConfiguration ?? throw new ArgumentNullException(nameof(ownerConfiguration));
    private readonly IChatUpdateIntake chatUpdateIntake =
        chatUpdateIntake ?? throw new ArgumentNullException(nameof(chatUpdateIntake));
    private readonly ITelegramPollingClientFactory pollingClientFactory =
        pollingClientFactory ?? throw new ArgumentNullException(nameof(pollingClientFactory));
    private readonly ILogger<TelegramPollingParticipant> logger =
        logger ?? throw new ArgumentNullException(nameof(logger));

    private CancellationTokenSource? pollingCancellationSource;
    private Task? pollingTask;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (pollingTask is not null)
        {
            throw new InvalidOperationException("Telegram polling has already been started.");
        }

        ITelegramPollingClient pollingClient = pollingClientFactory.Create(telegramConfiguration.BotToken);

        Log.DroppingPendingUpdates(logger);
        await pollingClient.DropPendingUpdatesAsync(cancellationToken);
        Log.PendingUpdatesDropped(logger);

        pollingCancellationSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        pollingTask = RunPollingLoopAsync(pollingClient, pollingCancellationSource.Token);
        Log.TelegramPollingStarted(logger);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (pollingTask is null || pollingCancellationSource is null)
        {
            return;
        }

        Log.TelegramPollingStopping(logger);
        pollingCancellationSource.Cancel();

        try
        {
            await pollingTask.WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (pollingCancellationSource.IsCancellationRequested)
        {
        }
        finally
        {
            pollingCancellationSource.Dispose();
            pollingCancellationSource = null;
            pollingTask = null;
        }

        Log.TelegramPollingStopped(logger);
    }

    private async Task RunPollingLoopAsync(ITelegramPollingClient pollingClient, CancellationToken cancellationToken)
    {
        int? offset = null;

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                IReadOnlyList<TelegramIncomingUpdate> updates = await pollingClient.GetUpdatesAsync(
                    offset,
                    telegramConfiguration.Polling.Timeout,
                    cancellationToken);

                foreach (TelegramIncomingUpdate update in updates)
                {
                    offset = update.UpdateId + 1;
                    await HandleUpdateAsync(update, cancellationToken);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                Log.TelegramPollingFailed(logger, exception);
                await Task.Delay(telegramConfiguration.Polling.RetryDelay, cancellationToken);
            }
        }
    }

    private async ValueTask HandleUpdateAsync(TelegramIncomingUpdate update, CancellationToken cancellationToken)
    {
        if (update.Message is null)
        {
            Log.UnsupportedUpdateSkipped(logger, update.UpdateId);
            return;
        }

        if (update.Message.UserId != ownerConfiguration.UserId.Value)
        {
            Log.UnauthorizedSenderIgnored(logger, update.Message.ChatId, update.Message.UserId);
            return;
        }

        InboundChatInput? input = TryParseInput(update.Message.Text);
        if (input is null)
        {
            Log.UnsupportedOwnerMessageIgnored(logger, update.Message.ChatId, update.Message.UserId);
            return;
        }

        InboundChatUpdate applicationUpdate = new(
            new ChatId(update.Message.ChatId),
            new UserId(update.Message.UserId),
            update.Message.Username,
            update.Message.SentAtUtc,
            input);

        await chatUpdateIntake.HandleAsync(applicationUpdate, cancellationToken);
    }

    private static InboundChatInput? TryParseInput(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        string normalizedText = text.Trim();
        if (!normalizedText.StartsWith('/'))
        {
            return new InboundChatTextMessage(normalizedText);
        }

        string[] segments = normalizedText.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        string commandToken = segments[0][1..];
        string commandName = commandToken.Split('@', 2, StringSplitOptions.RemoveEmptyEntries)[0];
        string[] arguments = segments.Skip(1).ToArray();
        return new InboundChatCommand(commandName, arguments, normalizedText);
    }

    private static partial class Log
    {
        [LoggerMessage(EventId = 300, Level = LogLevel.Information, Message = "Dropping pending Telegram updates before polling starts")]
        public static partial void DroppingPendingUpdates(ILogger logger);

        [LoggerMessage(EventId = 301, Level = LogLevel.Information, Message = "Dropped pending Telegram updates")]
        public static partial void PendingUpdatesDropped(ILogger logger);

        [LoggerMessage(EventId = 302, Level = LogLevel.Information, Message = "Telegram polling intake started")]
        public static partial void TelegramPollingStarted(ILogger logger);

        [LoggerMessage(EventId = 303, Level = LogLevel.Information, Message = "Telegram polling intake stopping")]
        public static partial void TelegramPollingStopping(ILogger logger);

        [LoggerMessage(EventId = 304, Level = LogLevel.Information, Message = "Telegram polling intake stopped")]
        public static partial void TelegramPollingStopped(ILogger logger);

        [LoggerMessage(EventId = 305, Level = LogLevel.Warning, Message = "Telegram polling failed and will retry")]
        public static partial void TelegramPollingFailed(ILogger logger, Exception exception);

        [LoggerMessage(EventId = 306, Level = LogLevel.Debug, Message = "Skipped unsupported Telegram update {UpdateId}")]
        public static partial void UnsupportedUpdateSkipped(ILogger logger, long updateId);

        [LoggerMessage(EventId = 307, Level = LogLevel.Warning, Message = "Ignored Telegram update from unauthorized user {UserId} in chat {ChatId}")]
        public static partial void UnauthorizedSenderIgnored(ILogger logger, long chatId, long userId);

        [LoggerMessage(EventId = 308, Level = LogLevel.Information, Message = "Ignored unsupported owner Telegram message from chat {ChatId} user {UserId}")]
        public static partial void UnsupportedOwnerMessageIgnored(ILogger logger, long chatId, long userId);
    }
}
