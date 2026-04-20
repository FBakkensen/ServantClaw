using Microsoft.Extensions.Options;

namespace ServantClaw.Host.Configuration;

public sealed class ValidateOwnerOptions : IValidateOptions<OwnerOptions>
{
    public ValidateOptionsResult Validate(string? name, OwnerOptions options)
    {
        if (options.UserId <= 0)
        {
            return ValidateOptionsResult.Fail(
                "Owner:UserId must be configured with the approved Telegram user ID.");
        }

        return ValidateOptionsResult.Success;
    }
}
