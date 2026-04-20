namespace ServantClaw.Domain.Configuration;

public sealed record BackendConfiguration
{
    public BackendConfiguration(string executablePath, string? workingDirectory = null, IReadOnlyList<string>? arguments = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);

        ExecutablePath = executablePath.Trim();
        WorkingDirectory = string.IsNullOrWhiteSpace(workingDirectory) ? null : workingDirectory.Trim();
        Arguments = arguments is null ? [] : [.. arguments];
    }

    public string ExecutablePath { get; }

    public string? WorkingDirectory { get; }

    public IReadOnlyList<string> Arguments { get; }
}
