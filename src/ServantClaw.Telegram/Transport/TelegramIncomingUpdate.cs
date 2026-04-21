namespace ServantClaw.Telegram.Transport;

public sealed record TelegramIncomingUpdate(int UpdateId, TelegramIncomingMessage? Message);
