using ServantClaw.Domain.Approvals;
using ServantClaw.Domain.Common;

namespace ServantClaw.Application.Approvals;

public interface IApprovalCoordinator
{
    ValueTask<ApprovalDecision> WaitForDecisionAsync(ApprovalRecord record, CancellationToken cancellationToken);

    ValueTask<ApprovalResolutionResult> ResolveAsync(
        ApprovalId approvalId,
        ChatId commandChatId,
        ApprovalDecision decision,
        CancellationToken cancellationToken);
}
