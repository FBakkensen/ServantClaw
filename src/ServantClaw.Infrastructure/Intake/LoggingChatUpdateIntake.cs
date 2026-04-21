using Microsoft.Extensions.Logging;
using ServantClaw.Application.Intake;
using ServantClaw.Application.Intake.Models;

namespace ServantClaw.Infrastructure.Intake;

public sealed partial class LoggingChatUpdateIntake(ILogger<LoggingChatUpdateIntake> logger) : IChatUpdateIntake
{
    public ValueTask HandleAsync(InboundChatUpdate update, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(update);

        switch (update.Input)
        {
            case InboundChatCommand command:
                Log.OwnerCommandAccepted(
                    logger,
                    update.ChatId.Value,
                    update.UserId.Value,
                    command.Name,
                    command.Arguments.Count);
                break;

            case InboundChatTextMessage message:
                Log.OwnerTextMessageAccepted(
                    logger,
                    update.ChatId.Value,
                    update.UserId.Value,
                    message.Text.Length);
                break;

            default:
                throw new InvalidOperationException($"Unsupported inbound chat input type '{update.Input.GetType().Name}'.");
        }

        return ValueTask.CompletedTask;
    }

    private static partial class Log
    {
        [LoggerMessage(
            EventId = 200,
            Level = LogLevel.Information,
            Message = "Accepted owner command {CommandName} from chat {ChatId} user {UserId} with {ArgumentCount} arguments")]
        public static partial void OwnerCommandAccepted(
            ILogger logger,
            long chatId,
            long userId,
            string commandName,
            int argumentCount);

        [LoggerMessage(
            EventId = 201,
            Level = LogLevel.Information,
            Message = "Accepted owner text message from chat {ChatId} user {UserId} with length {MessageLength}")]
        public static partial void OwnerTextMessageAccepted(
            ILogger logger,
            long chatId,
            long userId,
            int messageLength);
    }
}
