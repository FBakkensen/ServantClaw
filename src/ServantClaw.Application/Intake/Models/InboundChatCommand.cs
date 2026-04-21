namespace ServantClaw.Application.Intake.Models;

public sealed record InboundChatCommand(string Name, IReadOnlyList<string> Arguments, string RawText) : InboundChatInput
{
    public string Name { get; } = string.IsNullOrWhiteSpace(Name)
        ? throw new ArgumentException("Command name must be provided.", nameof(Name))
        : Name.Trim();

    public IReadOnlyList<string> Arguments { get; } = Arguments ?? throw new ArgumentNullException(nameof(Arguments));

    public string RawText { get; } = string.IsNullOrWhiteSpace(RawText)
        ? throw new ArgumentException("Raw command text must be provided.", nameof(RawText))
        : RawText.Trim();
}
