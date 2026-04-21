using ServantClaw.Application.Approvals;
using ServantClaw.Application.Intake.Models;
using ServantClaw.Application.Runtime;
using ServantClaw.Domain.Agents;
using ServantClaw.Domain.Approvals;
using ServantClaw.Domain.Common;
using ServantClaw.Domain.Routing;
using ServantClaw.Domain.State;

namespace ServantClaw.Application.Commands;

public sealed class ChatCommandProcessor(
    IStateStore stateStore,
    IProjectCatalog projectCatalog,
    ThreadMappingCoordinator threadMappingCoordinator,
    IApprovalCoordinator approvalCoordinator)
{
    private readonly IStateStore stateStore = stateStore ?? throw new ArgumentNullException(nameof(stateStore));
    private readonly IProjectCatalog projectCatalog = projectCatalog ?? throw new ArgumentNullException(nameof(projectCatalog));
    private readonly ThreadMappingCoordinator threadMappingCoordinator = threadMappingCoordinator ?? throw new ArgumentNullException(nameof(threadMappingCoordinator));
    private readonly IApprovalCoordinator approvalCoordinator = approvalCoordinator ?? throw new ArgumentNullException(nameof(approvalCoordinator));

    public async ValueTask<ChatCommandResult> ProcessAsync(InboundChatUpdate update, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(update);

        if (update.Input is not InboundChatCommand command)
        {
            throw new InvalidOperationException(
                $"Expected {nameof(InboundChatCommand)} but received '{update.Input.GetType().Name}'.");
        }

        return command.Name.Trim().ToLowerInvariant() switch
        {
            "agent" => await ProcessAgentCommandAsync(update, command, cancellationToken),
            "project" => await ProcessProjectCommandAsync(update, command, cancellationToken),
            "clear" => await ProcessClearCommandAsync(update, command, cancellationToken),
            "approve" => await ProcessApprovalDecisionAsync(update, command, ApprovalDecision.Approved, cancellationToken),
            "deny" => await ProcessApprovalDecisionAsync(update, command, ApprovalDecision.Denied, cancellationToken),
            _ => new ChatCommandResult($"Unsupported command '/{command.Name}'.")
        };
    }

    private async ValueTask<ChatCommandResult> ProcessAgentCommandAsync(
        InboundChatUpdate update,
        InboundChatCommand command,
        CancellationToken cancellationToken)
    {
        if (command.Arguments.Count != 1)
        {
            return new ChatCommandResult("Usage: /agent <agent-id>");
        }

        if (!TryParseAgent(command.Arguments[0], out AgentKind agent))
        {
            return new ChatCommandResult("Invalid agent id. Supported values: general, coding.");
        }

        ChatState currentState = await GetOrCreateChatStateAsync(update.ChatId, cancellationToken);
        ChatState updatedState = currentState.SetActiveAgent(agent);

        await stateStore.SaveChatStateAsync(updatedState, cancellationToken);
        return new ChatCommandResult($"Active agent set to '{ToAgentId(agent)}'.");
    }

    private async ValueTask<ChatCommandResult> ProcessProjectCommandAsync(
        InboundChatUpdate update,
        InboundChatCommand command,
        CancellationToken cancellationToken)
    {
        if (command.Arguments.Count != 2)
        {
            return new ChatCommandResult("Usage: /project <agent-id> <project-id>");
        }

        if (!TryParseAgent(command.Arguments[0], out AgentKind agent))
        {
            return new ChatCommandResult("Invalid agent id. Supported values: general, coding.");
        }

        ProjectId projectId;

        try
        {
            projectId = new ProjectId(command.Arguments[1]);
        }
        catch (ArgumentException)
        {
            return new ChatCommandResult("Project id must be provided.");
        }

        if (!await projectCatalog.ProjectExistsAsync(projectId, cancellationToken))
        {
            return new ChatCommandResult($"Unknown project '{projectId.Value}'.");
        }

        ChatState currentState = await GetOrCreateChatStateAsync(update.ChatId, cancellationToken);
        ChatState updatedState = currentState
            .BindProject(agent, projectId)
            .SetActiveAgent(agent);

        await stateStore.SaveChatStateAsync(updatedState, cancellationToken);

        return new ChatCommandResult(
            $"Active agent set to '{ToAgentId(agent)}'. Active project set to '{projectId.Value}'.");
    }

    private async ValueTask<ChatCommandResult> ProcessClearCommandAsync(
        InboundChatUpdate update,
        InboundChatCommand command,
        CancellationToken cancellationToken)
    {
        if (command.Arguments.Count != 0)
        {
            return new ChatCommandResult("Usage: /clear");
        }

        ChatState currentState = await GetOrCreateChatStateAsync(update.ChatId, cancellationToken);
        ProjectId? activeProject = currentState.GetActiveProject();
        if (activeProject is null)
        {
            return new ChatCommandResult(await BuildMissingProjectMessageAsync(currentState.ActiveAgent, cancellationToken));
        }

        ProjectId selectedProject = activeProject.Value;
        ThreadContext context = new(update.ChatId, currentState.ActiveAgent, selectedProject);
        await threadMappingCoordinator.RotateAsync(context, cancellationToken);

        return new ChatCommandResult(
            $"Started a fresh thread for agent '{ToAgentId(currentState.ActiveAgent)}' and project '{selectedProject.Value}'.");
    }

    private async ValueTask<ChatCommandResult> ProcessApprovalDecisionAsync(
        InboundChatUpdate update,
        InboundChatCommand command,
        ApprovalDecision decision,
        CancellationToken cancellationToken)
    {
        string commandName = decision == ApprovalDecision.Approved ? "approve" : "deny";

        if (command.Arguments.Count != 1 || string.IsNullOrWhiteSpace(command.Arguments[0]))
        {
            return new ChatCommandResult($"Usage: /{commandName} <approval-id>");
        }

        ApprovalId approvalId = new(command.Arguments[0]);
        ApprovalResolutionResult result = await approvalCoordinator
            .ResolveAsync(approvalId, update.ChatId, decision, cancellationToken);

        return new ChatCommandResult(result.Message);
    }

    private async ValueTask<ChatState> GetOrCreateChatStateAsync(ChatId chatId, CancellationToken cancellationToken) =>
        await stateStore.GetChatStateAsync(chatId, cancellationToken)
        ?? new ChatState(chatId, AgentKind.General, new AgentProjectBindings());

    private async ValueTask<string> BuildMissingProjectMessageAsync(AgentKind activeAgent, CancellationToken cancellationToken)
    {
        IReadOnlyCollection<ProjectId> availableProjects = await projectCatalog.ListProjectsAsync(cancellationToken);
        string availableProjectsText = availableProjects.Count == 0
            ? "No projects are currently available."
            : $"Available projects: {string.Join(", ", availableProjects.Select(projectId => projectId.Value))}.";

        return
            $"No active project is selected for agent '{ToAgentId(activeAgent)}'. Use /project <agent-id> <project-id> before sending normal messages. {availableProjectsText}";
    }

    private static bool TryParseAgent(string value, out AgentKind agent)
    {
        switch (value.Trim().ToLowerInvariant())
        {
            case "general":
                agent = AgentKind.General;
                return true;

            case "coding":
                agent = AgentKind.Coding;
                return true;

            default:
                agent = default;
                return false;
        }
    }

    private static string ToAgentId(AgentKind agent) => agent switch
    {
        AgentKind.General => "general",
        AgentKind.Coding => "coding",
        _ => throw new ArgumentOutOfRangeException(nameof(agent), agent, "Unsupported agent kind.")
    };
}
