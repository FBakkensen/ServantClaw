using System.ComponentModel.DataAnnotations;
using ServantClaw.Domain.Configuration;

namespace ServantClaw.Host.Configuration;

public sealed class BackendOptions
{
    [Required]
    public string ExecutablePath { get; set; } = string.Empty;

    public string? WorkingDirectory { get; set; }

    public string[] Arguments { get; set; } = [];

    public BackendConfiguration ToDomainConfiguration() =>
        new(ExecutablePath, WorkingDirectory, Arguments);
}
