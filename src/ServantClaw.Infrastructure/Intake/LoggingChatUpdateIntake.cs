using ServantClaw.Application.Commands;
using Microsoft.Extensions.Logging;
using ServantClaw.Application.Intake;
using ServantClaw.Application.Intake.Models;

namespace ServantClaw.Infrastructure.Intake;

public sealed partial class LoggingChatUpdateIntake(
    ChatCommandProcessor commandProcessor,
    IChatReplySink chatReplySink,
    ILogger<LoggingChatUpdateIntake> logger) : IChatUpdateIntake
{
    private readonly ChatCommandProcessor commandProcessor = commandProcessor ?? throw new ArgumentNullException(nameof(commandProcessor));
    private readonly IChatReplySink chatReplySink = chatReplySink ?? throw new ArgumentNullException(nameof(chatReplySink));

    public ValueTask HandleAsync(InboundChatUpdate update, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(update);
        return update.Input switch
        {
            InboundChatCommand command => HandleCommandAsync(update, command, cancellationToken),
            InboundChatTextMessage message => HandleTextMessageAsync(update, message),
            _ => throw new InvalidOperationException($"Unsupported inbound chat input type '{update.Input.GetType().Name}'.")
        };
    }

    private async ValueTask HandleCommandAsync(
        InboundChatUpdate update,
        InboundChatCommand command,
        CancellationToken cancellationToken)
    {
        Log.OwnerCommandAccepted(
            logger,
            update.ChatId.Value,
            update.UserId.Value,
            command.Name,
            command.Arguments.Count);

        ChatCommandResult result = await commandProcessor.ProcessAsync(update, cancellationToken);
        await chatReplySink.SendMessageAsync(update.ChatId, result.ResponseText, cancellationToken);
    }

    private ValueTask HandleTextMessageAsync(InboundChatUpdate update, InboundChatTextMessage message)
    {
        Log.OwnerTextMessageAccepted(
            logger,
            update.ChatId.Value,
            update.UserId.Value,
            message.Text.Length);

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
