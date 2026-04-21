using System.Diagnostics.CodeAnalysis;

namespace ServantClaw.Domain.Configuration;

[ExcludeFromCodeCoverage]
public sealed record ServiceConfiguration
{
    public ServiceConfiguration(string botRootPath, string projectsRootPath, BackendConfiguration backend)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(botRootPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(projectsRootPath);

        BotRootPath = botRootPath.Trim();
        ProjectsRootPath = projectsRootPath.Trim();
        Backend = backend ?? throw new ArgumentNullException(nameof(backend));
    }

    public string BotRootPath { get; }

    public string ProjectsRootPath { get; }

    public BackendConfiguration Backend { get; }
}
