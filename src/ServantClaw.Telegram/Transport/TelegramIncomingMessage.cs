namespace ServantClaw.Telegram.Transport;

public sealed record TelegramIncomingMessage(
    long ChatId,
    long UserId,
    string? Username,
    DateTimeOffset SentAtUtc,
    string? Text)
{
    public string? Username { get; } = string.IsNullOrWhiteSpace(Username) ? null : Username.Trim();
}
