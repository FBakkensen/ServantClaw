using ServantClaw.Domain.Agents;
using ServantClaw.Domain.Common;

namespace ServantClaw.Domain.Routing;

public sealed record ChatState
{
    public ChatState(ChatId chatId, AgentKind activeAgent, AgentProjectBindings projectBindings)
    {
        ChatId = chatId;
        ActiveAgent = activeAgent;
        ProjectBindings = projectBindings ?? throw new ArgumentNullException(nameof(projectBindings));
    }

    public ChatId ChatId { get; init; }

    public AgentKind ActiveAgent { get; init; }

    public AgentProjectBindings ProjectBindings { get; init; }

    public ChatState SetActiveAgent(AgentKind activeAgent) => this with { ActiveAgent = activeAgent };

    public ChatState BindProject(AgentKind agent, ProjectId projectId) => this with
    {
        ProjectBindings = ProjectBindings.SetProject(agent, projectId)
    };

    public ProjectId? GetActiveProject() => ProjectBindings.GetProject(ActiveAgent);
}
