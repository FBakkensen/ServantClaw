using ServantClaw.Application.Intake.Models;
using ServantClaw.Domain.Agents;
using ServantClaw.Domain.Common;
using ServantClaw.Domain.Routing;
using ServantClaw.Domain.State;

namespace ServantClaw.Application.Commands;

public sealed class ChatCommandProcessor(IStateStore stateStore, IProjectCatalog projectCatalog)
{
    private readonly IStateStore stateStore = stateStore ?? throw new ArgumentNullException(nameof(stateStore));
    private readonly IProjectCatalog projectCatalog = projectCatalog ?? throw new ArgumentNullException(nameof(projectCatalog));

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

    private async ValueTask<ChatState> GetOrCreateChatStateAsync(ChatId chatId, CancellationToken cancellationToken) =>
        await stateStore.GetChatStateAsync(chatId, cancellationToken)
        ?? new ChatState(chatId, AgentKind.General, new AgentProjectBindings());

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
