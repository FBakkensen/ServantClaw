using System.Diagnostics.CodeAnalysis;

namespace ServantClaw.Application.Intake.Models;

[ExcludeFromCodeCoverage]
public sealed record InboundChatTextMessage(string Text) : InboundChatInput
{
    public string Text { get; } = string.IsNullOrWhiteSpace(Text)
        ? throw new ArgumentException("Message text must be provided.", nameof(Text))
        : Text.Trim();
}
