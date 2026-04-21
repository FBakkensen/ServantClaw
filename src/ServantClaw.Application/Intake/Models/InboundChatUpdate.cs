using ServantClaw.Domain.Common;

namespace ServantClaw.Application.Intake.Models;

public sealed record InboundChatUpdate(
    ChatId ChatId,
    UserId UserId,
    string? Username,
    DateTimeOffset ReceivedAtUtc,
    InboundChatInput Input)
{
    public string? Username { get; } = string.IsNullOrWhiteSpace(Username) ? null : Username.Trim();

    public InboundChatInput Input { get; } = Input ?? throw new ArgumentNullException(nameof(Input));
}
