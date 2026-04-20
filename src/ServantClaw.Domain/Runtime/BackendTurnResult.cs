using ServantClaw.Domain.Approvals;

namespace ServantClaw.Domain.Runtime;

public sealed record BackendTurnResult(string? FinalResponse, ApprovalRecord? RequestedApproval)
{
    public bool RequiresApproval => RequestedApproval is not null;
}
