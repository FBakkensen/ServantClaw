using Microsoft.Extensions.Logging;
using ServantClaw.Application.Approvals;
using ServantClaw.Application.Commands;
using ServantClaw.Domain.Approvals;
using ServantClaw.Domain.Common;
using ServantClaw.Domain.Routing;
using ServantClaw.Domain.Runtime;
using ServantClaw.Domain.State;

namespace ServantClaw.Application.Runtime;

public sealed partial class CodexTurnExecutor(
    IBackendClient backendClient,
    IStateStore stateStore,
    IApprovalCoordinator approvalCoordinator,
    IChatReplySink chatReplySink,
    ILogger<CodexTurnExecutor> logger) : ITurnExecutor
{
    public const string BackendUnavailableReply = "The Codex backend is currently unavailable. Please try again in a moment.";
    public const string GenericFailureReply = "An unexpected error occurred while running the turn. Please try again.";
    public const string EmptyResponseReply = "The assistant completed the turn without a text response.";

    private readonly IBackendClient backendClient = backendClient ?? throw new ArgumentNullException(nameof(backendClient));
    private readonly IStateStore stateStore = stateStore ?? throw new ArgumentNullException(nameof(stateStore));
    private readonly IApprovalCoordinator approvalCoordinator = approvalCoordinator ?? throw new ArgumentNullException(nameof(approvalCoordinator));
    private readonly IChatReplySink chatReplySink = chatReplySink ?? throw new ArgumentNullException(nameof(chatReplySink));
    private readonly ILogger<CodexTurnExecutor> logger = logger ?? throw new ArgumentNullException(nameof(logger));

    public async ValueTask ExecuteAsync(QueuedTurn turn, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(turn);
        ThreadContext context = turn.Context;

        Log.TurnStarted(logger, context.ChatId.Value, context.Agent.ToString(), context.ProjectId.Value, turn.MessageText.Length);

        try
        {
            await backendClient.EnsureBackendReadyAsync(cancellationToken).ConfigureAwait(false);
            await PrepareThreadAsync(context, cancellationToken).ConfigureAwait(false);

            BackendTurnResult result = await backendClient
                .SendTurnAsync(new BackendTurnRequest(context, turn.MessageText), cancellationToken)
                .ConfigureAwait(false);

            while (result.RequiresApproval)
            {
                ApprovalRecord pending = result.RequestedApproval!;
                ApprovalDecision decision = await approvalCoordinator
                    .WaitForDecisionAsync(pending, cancellationToken)
                    .ConfigureAwait(false);

                Log.ApprovalReceived(logger, context.ChatId.Value, pending.ApprovalId.Value, decision.ToString());

                result = await backendClient
                    .ContinueApprovedActionAsync(pending.ApprovalId, decision, cancellationToken)
                    .ConfigureAwait(false);
            }

            await DeliverFinalResponseAsync(context, result.FinalResponse, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (BackendUnavailableException exception)
        {
            Log.TurnFailedUnavailable(logger, context.ChatId.Value, context.Agent.ToString(), context.ProjectId.Value, exception);
            await chatReplySink.SendMessageAsync(context.ChatId, BackendUnavailableReply, cancellationToken).ConfigureAwait(false);
        }
        catch (BackendTurnFailedException exception)
        {
            Log.TurnFailedCodex(logger, context.ChatId.Value, context.Agent.ToString(), context.ProjectId.Value, exception);
            await chatReplySink.SendMessageAsync(
                context.ChatId,
                $"The assistant couldn't complete the turn: {exception.Message}",
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            Log.TurnFailedUnexpected(logger, context.ChatId.Value, context.Agent.ToString(), context.ProjectId.Value, exception);
            await chatReplySink.SendMessageAsync(context.ChatId, GenericFailureReply, cancellationToken).ConfigureAwait(false);
        }
    }

    private async ValueTask PrepareThreadAsync(ThreadContext context, CancellationToken cancellationToken)
    {
        ThreadMapping? existing = await stateStore.GetThreadMappingAsync(context, cancellationToken).ConfigureAwait(false);
        if (existing?.CurrentThread is ThreadReference current)
        {
            await backendClient.ResumeThreadAsync(current, cancellationToken).ConfigureAwait(false);
            Log.ThreadResumed(logger, context.ChatId.Value, context.Agent.ToString(), context.ProjectId.Value, current.Value);
            return;
        }

        ThreadReference created = await backendClient.CreateThreadAsync(context, cancellationToken).ConfigureAwait(false);
        ThreadMapping updated = existing?.WithCurrentThread(created) ?? new ThreadMapping(context, created);
        await stateStore.SaveThreadMappingAsync(updated, cancellationToken).ConfigureAwait(false);
        Log.ThreadCreated(logger, context.ChatId.Value, context.Agent.ToString(), context.ProjectId.Value, created.Value);
    }

    private async ValueTask DeliverFinalResponseAsync(ThreadContext context, string? finalResponse, CancellationToken cancellationToken)
    {
        string textToSend = string.IsNullOrWhiteSpace(finalResponse) ? EmptyResponseReply : finalResponse;
        await chatReplySink.SendMessageAsync(context.ChatId, textToSend, cancellationToken).ConfigureAwait(false);
        Log.TurnCompleted(logger, context.ChatId.Value, context.Agent.ToString(), context.ProjectId.Value, textToSend.Length);
    }

    private static partial class Log
    {
        [LoggerMessage(
            EventId = 420,
            Level = LogLevel.Information,
            Message = "Starting turn for chat {ChatId} agent {Agent} project {ProjectId} with message length {MessageLength}")]
        public static partial void TurnStarted(ILogger logger, long chatId, string agent, string projectId, int messageLength);

        [LoggerMessage(
            EventId = 421,
            Level = LogLevel.Information,
            Message = "Completed turn for chat {ChatId} agent {Agent} project {ProjectId} with reply length {ReplyLength}")]
        public static partial void TurnCompleted(ILogger logger, long chatId, string agent, string projectId, int replyLength);

        [LoggerMessage(
            EventId = 422,
            Level = LogLevel.Information,
            Message = "Created new Codex thread {ThreadId} for chat {ChatId} agent {Agent} project {ProjectId}")]
        public static partial void ThreadCreated(ILogger logger, long chatId, string agent, string projectId, string threadId);

        [LoggerMessage(
            EventId = 423,
            Level = LogLevel.Information,
            Message = "Resumed Codex thread {ThreadId} for chat {ChatId} agent {Agent} project {ProjectId}")]
        public static partial void ThreadResumed(ILogger logger, long chatId, string agent, string projectId, string threadId);

        [LoggerMessage(
            EventId = 424,
            Level = LogLevel.Information,
            Message = "Approval {ApprovalId} resolved with decision {Decision} for chat {ChatId}")]
        public static partial void ApprovalReceived(ILogger logger, long chatId, string approvalId, string decision);

        [LoggerMessage(
            EventId = 426,
            Level = LogLevel.Warning,
            Message = "Turn failed because the backend was unavailable for chat {ChatId} agent {Agent} project {ProjectId}")]
        public static partial void TurnFailedUnavailable(ILogger logger, long chatId, string agent, string projectId, Exception exception);

        [LoggerMessage(
            EventId = 427,
            Level = LogLevel.Warning,
            Message = "Turn failed because Codex reported a turn failure for chat {ChatId} agent {Agent} project {ProjectId}")]
        public static partial void TurnFailedCodex(ILogger logger, long chatId, string agent, string projectId, Exception exception);

        [LoggerMessage(
            EventId = 428,
            Level = LogLevel.Error,
            Message = "Turn failed with an unexpected error for chat {ChatId} agent {Agent} project {ProjectId}")]
        public static partial void TurnFailedUnexpected(ILogger logger, long chatId, string agent, string projectId, Exception exception);
    }
}
