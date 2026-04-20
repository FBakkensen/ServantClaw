using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Options;
using ServantClaw.Domain.Configuration;

namespace ServantClaw.Host.Configuration;

public sealed class TelegramOptions
{
    public const string SectionName = "Telegram";
    public const string BotTokenPlaceholder = "__SET_TELEGRAM_BOT_TOKEN__";

    [Required]
    public string BotToken { get; set; } = string.Empty;

    [Required]
    [ValidateObjectMembers]
    public PollingOptions Polling { get; set; } = new();

    public TelegramConfiguration ToDomainConfiguration() =>
        new(BotToken, Polling.ToDomainConfiguration());
}
