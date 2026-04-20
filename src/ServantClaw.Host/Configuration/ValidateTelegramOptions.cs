using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace ServantClaw.Host.Configuration;

public sealed partial class ValidateTelegramOptions : IValidateOptions<TelegramOptions>
{
    public ValidateOptionsResult Validate(string? name, TelegramOptions options)
    {
        List<string> failures = [];

        if (string.IsNullOrWhiteSpace(options.BotToken))
        {
            failures.Add("Telegram:BotToken must be configured.");
        }
        else
        {
            string botToken = options.BotToken.Trim();

            if (string.Equals(botToken, TelegramOptions.BotTokenPlaceholder, StringComparison.Ordinal))
            {
                failures.Add(
                    "Telegram:BotToken still uses the placeholder value. Replace it with the real Telegram bot token.");
            }
            else if (!TelegramBotTokenRegex().IsMatch(botToken))
            {
                failures.Add("Telegram:BotToken must look like a valid Telegram bot token.");
            }
        }

        if (options.Polling.Timeout <= TimeSpan.Zero)
        {
            failures.Add("Telegram:Polling:Timeout must be greater than zero.");
        }

        if (options.Polling.RetryDelay < TimeSpan.Zero)
        {
            failures.Add("Telegram:Polling:RetryDelay cannot be negative.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

    [GeneratedRegex(@"^\d{6,}:[A-Za-z0-9_-]{20,}$")]
    private static partial Regex TelegramBotTokenRegex();
}
