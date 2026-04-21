namespace ServantClaw.Application.Commands;

public sealed record ChatCommandResult(string ResponseText)
{
    public string ResponseText { get; } = string.IsNullOrWhiteSpace(ResponseText)
        ? throw new ArgumentException("Response text must be provided.", nameof(ResponseText))
        : ResponseText.Trim();
}
