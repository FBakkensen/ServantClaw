using System.Diagnostics.CodeAnalysis;
using ServantClaw.Domain.Approvals;

namespace ServantClaw.Domain.Runtime;

[ExcludeFromCodeCoverage]
public sealed record BackendTurnResult(string? FinalResponse, ApprovalRecord? RequestedApproval)
{
    public bool RequiresApproval => RequestedApproval is not null;
}
