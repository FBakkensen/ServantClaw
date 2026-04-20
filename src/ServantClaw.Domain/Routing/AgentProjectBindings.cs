using ServantClaw.Domain.Agents;
using ServantClaw.Domain.Common;

namespace ServantClaw.Domain.Routing;

public sealed record AgentProjectBindings(ProjectId? GeneralProjectId = null, ProjectId? CodingProjectId = null)
{
    public ProjectId? GetProject(AgentKind agent) => agent switch
    {
        AgentKind.General => GeneralProjectId,
        AgentKind.Coding => CodingProjectId,
        _ => throw new ArgumentOutOfRangeException(nameof(agent), agent, "Unsupported agent kind.")
    };

    public AgentProjectBindings SetProject(AgentKind agent, ProjectId projectId) => agent switch
    {
        AgentKind.General => this with { GeneralProjectId = projectId },
        AgentKind.Coding => this with { CodingProjectId = projectId },
        _ => throw new ArgumentOutOfRangeException(nameof(agent), agent, "Unsupported agent kind.")
    };
}
