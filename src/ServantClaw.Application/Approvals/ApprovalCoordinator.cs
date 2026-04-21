using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using ServantClaw.Application.Commands;
using ServantClaw.Domain.Approvals;
using ServantClaw.Domain.Common;
using ServantClaw.Domain.Runtime;
using ServantClaw.Domain.State;

namespace ServantClaw.Application.Approvals;

public sealed partial class ApprovalCoordinator : IApprovalCoordinator
{
    private readonly IStateStore stateStore;
    private readonly IChatReplySink chatReplySink;
    private readonly IClock clock;
    private readonly ILogger<ApprovalCoordinator> logger;
    private readonly ConcurrentDictionary<ApprovalId, TaskCompletionSource<ApprovalDecision>> pendingDecisions = new();

    public ApprovalCoordinator(
        IStateStore stateStore,
        IChatReplySink chatReplySink,
        IClock clock,
        ILogger<ApprovalCoordinator> logger)
    {
        this.stateStore = stateStore ?? throw new ArgumentNullException(nameof(stateStore));
        this.chatReplySink = chatReplySink ?? throw new ArgumentNullException(nameof(chatReplySink));
        this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async ValueTask<ApprovalDecision> WaitForDecisionAsync(ApprovalRecord record, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(record);

        await stateStore.SaveApprovalAsync(record, cancellationToken).ConfigureAwait(false);

        TaskCompletionSource<ApprovalDecision> decisionSource = new(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!pendingDecisions.TryAdd(record.ApprovalId, decisionSource))
        {
            throw new InvalidOperationException(
                $"Approval '{record.ApprovalId.Value}' is already awaiting a decision.");
        }

        try
        {
            await chatReplySink.SendMessageAsync(
                record.Context.ChatId,
                BuildNotification(record),
                cancellationToken).ConfigureAwait(false);

            Log.ApprovalPending(logger, record.Context.ChatId.Value, record.ApprovalId.Value);

            return await decisionSource.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            pendingDecisions.TryRemove(record.ApprovalId, out _);
        }
    }

    public async ValueTask<ApprovalResolutionResult> ResolveAsync(
        ApprovalId approvalId,
        ChatId commandChatId,
        ApprovalDecision decision,
        CancellationToken cancellationToken)
    {
        ApprovalRecord? stored = await stateStore.GetApprovalAsync(approvalId, cancellationToken).ConfigureAwait(false);

        if (stored is null)
        {
            return new ApprovalResolutionResult(
                ApprovalResolutionOutcome.UnknownId,
                $"Approval '{approvalId.Value}' was not found.");
        }

        if (!stored.IsPending)
        {
            string priorDecisionText = stored.Decision == ApprovalDecision.Approved ? "approved" : "denied";
            return new ApprovalResolutionResult(
                ApprovalResolutionOutcome.AlreadyResolved,
                $"Approval '{approvalId.Value}' was already {priorDecisionText}.");
        }

        if (!stored.Context.ChatId.Equals(commandChatId))
        {
            return new ApprovalResolutionResult(
                ApprovalResolutionOutcome.WrongChat,
                $"Approval '{approvalId.Value}' does not belong to this chat.");
        }

        if (!pendingDecisions.TryRemove(approvalId, out TaskCompletionSource<ApprovalDecision>? decisionSource))
        {
            return new ApprovalResolutionResult(
                ApprovalResolutionOutcome.NotActive,
                $"Approval '{approvalId.Value}' is no longer active.");
        }

        try
        {
            ApprovalRecord resolved = stored.Resolve(decision, clock.UtcNow);
            await stateStore.SaveApprovalAsync(resolved, cancellationToken).ConfigureAwait(false);
            decisionSource.TrySetResult(decision);
            Log.ApprovalResolved(logger, commandChatId.Value, approvalId.Value, decision.ToString());

            string ack = decision == ApprovalDecision.Approved
                ? $"Approval '{approvalId.Value}' accepted. The assistant is resuming the turn."
                : $"Approval '{approvalId.Value}' denied.";

            return new ApprovalResolutionResult(ApprovalResolutionOutcome.Resolved, ack);
        }
        catch (Exception exception)
        {
            decisionSource.TrySetException(exception);
            throw;
        }
    }

    private static string BuildNotification(ApprovalRecord record) =>
        $"Approval required ({record.ApprovalId.Value}): {record.Summary}. Reply with /approve {record.ApprovalId.Value} or /deny {record.ApprovalId.Value}.";

    private static partial class Log
    {
        [LoggerMessage(
            EventId = 500,
            Level = LogLevel.Information,
            Message = "Approval {ApprovalId} pending for chat {ChatId}")]
        public static partial void ApprovalPending(ILogger logger, long chatId, string approvalId);

        [LoggerMessage(
            EventId = 501,
            Level = LogLevel.Information,
            Message = "Approval {ApprovalId} resolved with decision {Decision} from chat {ChatId}")]
        public static partial void ApprovalResolved(ILogger logger, long chatId, string approvalId, string decision);
    }
}
