using ServantClaw.Domain.Common;

namespace ServantClaw.Application.Commands;

public interface IChatReplySink
{
    ValueTask SendMessageAsync(ChatId chatId, string message, CancellationToken cancellationToken);
}
