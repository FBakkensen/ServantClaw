using ServantClaw.Application.Commands;
using ServantClaw.Domain.Common;
using ServantClaw.Domain.Configuration;

namespace ServantClaw.Infrastructure.Commands;

public sealed class FileSystemProjectCatalog(ServiceConfiguration serviceConfiguration) : IProjectCatalog
{
    private readonly string projectsRootPath = (serviceConfiguration ?? throw new ArgumentNullException(nameof(serviceConfiguration))).ProjectsRootPath;

    public ValueTask<bool> ProjectExistsAsync(ProjectId projectId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        string projectPath = Path.Combine(projectsRootPath, projectId.Value);
        return ValueTask.FromResult(Directory.Exists(projectPath));
    }
}
