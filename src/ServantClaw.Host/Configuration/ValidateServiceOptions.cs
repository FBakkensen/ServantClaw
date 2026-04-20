using Microsoft.Extensions.Options;

namespace ServantClaw.Host.Configuration;

public sealed class ValidateServiceOptions : IValidateOptions<ServiceOptions>
{
    public ValidateOptionsResult Validate(string? name, ServiceOptions options)
    {
        List<string> failures = [];

        if (string.IsNullOrWhiteSpace(options.BotRootPath))
        {
            failures.Add("Service:BotRootPath must be configured.");
        }

        if (string.IsNullOrWhiteSpace(options.ProjectsRootPath))
        {
            failures.Add("Service:ProjectsRootPath must be configured.");
        }

        if (string.IsNullOrWhiteSpace(options.Backend.ExecutablePath))
        {
            failures.Add("Service:Backend:ExecutablePath must be configured.");
        }
        else if (string.Equals(
                     options.Backend.ExecutablePath.Trim(),
                     ServiceOptions.BackendExecutablePlaceholder,
                     StringComparison.Ordinal))
        {
            failures.Add(
                "Service:Backend:ExecutablePath still uses the placeholder value. Replace it with the Codex backend executable path.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
