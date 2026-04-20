using ServantClaw.Domain.Approvals;
using ServantClaw.Domain.Common;
using ServantClaw.Domain.Routing;

namespace ServantClaw.Domain.Runtime;

public interface IBackendClient
{
    ValueTask EnsureBackendReadyAsync(CancellationToken cancellationToken);

    ValueTask<ThreadReference> CreateThreadAsync(ThreadContext context, CancellationToken cancellationToken);

    ValueTask ResumeThreadAsync(ThreadReference threadReference, CancellationToken cancellationToken);

    ValueTask<BackendTurnResult> SendTurnAsync(BackendTurnRequest request, CancellationToken cancellationToken);

    ValueTask<BackendTurnResult> ContinueApprovedActionAsync(ApprovalId approvalId, ApprovalDecision decision, CancellationToken cancellationToken);

    ValueTask<BackendHealth> GetBackendHealthAsync(CancellationToken cancellationToken);
}
