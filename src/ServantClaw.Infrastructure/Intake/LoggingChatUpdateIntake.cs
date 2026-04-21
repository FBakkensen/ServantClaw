using ServantClaw.Application.Commands;
using Microsoft.Extensions.Logging;
using ServantClaw.Application.Intake;
using ServantClaw.Application.Intake.Models;
using ServantClaw.Application.Runtime;
using ServantClaw.Domain.Common;
using ServantClaw.Domain.Routing;
using ServantClaw.Domain.State;

namespace ServantClaw.Infrastructure.Intake;

public sealed partial class LoggingChatUpdateIntake(
    ChatCommandProcessor commandProcessor,
    ThreadMappingCoordinator threadMappingCoordinator,
    IStateStore stateStore,
    IProjectCatalog projectCatalog,
    IChatReplySink chatReplySink,
    ILogger<LoggingChatUpdateIntake> logger) : IChatUpdateIntake
{
    private readonly ChatCommandProcessor commandProcessor = commandProcessor ?? throw new ArgumentNullException(nameof(commandProcessor));
    private readonly ThreadMappingCoordinator threadMappingCoordinator = threadMappingCoordinator ?? throw new ArgumentNullException(nameof(threadMappingCoordinator));
    private readonly IStateStore stateStore = stateStore ?? throw new ArgumentNullException(nameof(stateStore));
    private readonly IProjectCatalog projectCatalog = projectCatalog ?? throw new ArgumentNullException(nameof(projectCatalog));
    private readonly IChatReplySink chatReplySink = chatReplySink ?? throw new ArgumentNullException(nameof(chatReplySink));

    public ValueTask HandleAsync(InboundChatUpdate update, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(update);
        return update.Input switch
        {
            InboundChatCommand command => HandleCommandAsync(update, command, cancellationToken),
            InboundChatTextMessage message => HandleTextMessageAsync(update, message, cancellationToken),
            _ => throw new InvalidOperationException($"Unsupported inbound chat input type '{update.Input.GetType().Name}'.")
        };
    }

    private async ValueTask HandleCommandAsync(
        InboundChatUpdate update,
        InboundChatCommand command,
        CancellationToken cancellationToken)
    {
        Log.OwnerCommandAccepted(
            logger,
            update.ChatId.Value,
            update.UserId.Value,
            command.Name,
            command.Arguments.Count);

        ChatCommandResult result = await commandProcessor.ProcessAsync(update, cancellationToken);
        await chatReplySink.SendMessageAsync(update.ChatId, result.ResponseText, cancellationToken);
    }

    private async ValueTask HandleTextMessageAsync(
        InboundChatUpdate update,
        InboundChatTextMessage message,
        CancellationToken cancellationToken)
    {
        Log.OwnerTextMessageAccepted(
            logger,
            update.ChatId.Value,
            update.UserId.Value,
            message.Text.Length);

        ChatState? state = await stateStore.GetChatStateAsync(update.ChatId, cancellationToken);
        ProjectId? activeProject = state?.GetActiveProject();
        if (activeProject is not null)
        {
            ThreadContext context = new(update.ChatId, state!.ActiveAgent, activeProject.Value);
            await threadMappingCoordinator.ResolveAsync(context, cancellationToken);
            return;
        }

        IReadOnlyCollection<ProjectId> availableProjects = await projectCatalog.ListProjectsAsync(cancellationToken);
        string availableProjectsText = availableProjects.Count == 0
            ? "No projects are currently available."
            : $"Available projects: {string.Join(", ", availableProjects.Select(projectId => projectId.Value))}.";

        await chatReplySink.SendMessageAsync(
            update.ChatId,
            $"No active project is selected for agent '{ToAgentId(state?.ActiveAgent ?? Domain.Agents.AgentKind.General)}'. Use /project <agent-id> <project-id> before sending normal messages. {availableProjectsText}",
            cancellationToken);
    }

    private static string ToAgentId(Domain.Agents.AgentKind agent) => agent switch
    {
        Domain.Agents.AgentKind.General => "general",
        Domain.Agents.AgentKind.Coding => "coding",
        _ => throw new ArgumentOutOfRangeException(nameof(agent), agent, "Unsupported agent kind.")
    };

    private static partial class Log
    {
        [LoggerMessage(
            EventId = 200,
            Level = LogLevel.Information,
            Message = "Accepted owner command {CommandName} from chat {ChatId} user {UserId} with {ArgumentCount} arguments")]
        public static partial void OwnerCommandAccepted(
            ILogger logger,
            long chatId,
            long userId,
            string commandName,
            int argumentCount);

        [LoggerMessage(
            EventId = 201,
            Level = LogLevel.Information,
            Message = "Accepted owner text message from chat {ChatId} user {UserId} with length {MessageLength}")]
        public static partial void OwnerTextMessageAccepted(
            ILogger logger,
            long chatId,
            long userId,
            int messageLength);
    }
}
