using ServantClaw.Domain.Common;

namespace ServantClaw.Application.Commands;

public interface IProjectCatalog
{
    ValueTask<bool> ProjectExistsAsync(ProjectId projectId, CancellationToken cancellationToken);
}
