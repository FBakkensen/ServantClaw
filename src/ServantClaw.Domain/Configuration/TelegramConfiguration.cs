using System.Diagnostics.CodeAnalysis;

namespace ServantClaw.Domain.Configuration;

[ExcludeFromCodeCoverage]
public sealed record TelegramConfiguration
{
    public TelegramConfiguration(string botToken, PollingConfiguration polling)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(botToken);

        BotToken = botToken.Trim();
        Polling = polling ?? throw new ArgumentNullException(nameof(polling));
    }

    public string BotToken { get; }

    public PollingConfiguration Polling { get; }
}
