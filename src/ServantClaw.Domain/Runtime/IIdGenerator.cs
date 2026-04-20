using ServantClaw.Domain.Common;

namespace ServantClaw.Domain.Runtime;

public interface IIdGenerator
{
    ApprovalId CreateApprovalId();
}
