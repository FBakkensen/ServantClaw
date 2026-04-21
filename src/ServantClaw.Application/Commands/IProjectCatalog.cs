using ServantClaw.Domain.Common;

namespace ServantClaw.Application.Commands;

public interface IProjectCatalog
{
    ValueTask<bool> ProjectExistsAsync(ProjectId projectId, CancellationToken cancellationToken);

    ValueTask<IReadOnlyCollection<ProjectId>> ListProjectsAsync(CancellationToken cancellationToken);
}
