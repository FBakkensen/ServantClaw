namespace ServantClaw.Telegram.Transport;

public interface ITelegramPollingClientFactory
{
    ITelegramPollingClient Create(string botToken);
}
