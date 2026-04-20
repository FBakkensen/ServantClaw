using ServantClaw.Domain.Approvals;
using ServantClaw.Domain.Common;
using ServantClaw.Domain.Configuration;
using ServantClaw.Domain.Routing;

namespace ServantClaw.Domain.State;

public interface IStateStore
{
    ValueTask<ChatState?> GetChatStateAsync(ChatId chatId, CancellationToken cancellationToken);

    ValueTask SaveChatStateAsync(ChatState chatState, CancellationToken cancellationToken);

    ValueTask<ThreadMapping?> GetThreadMappingAsync(ThreadContext context, CancellationToken cancellationToken);

    ValueTask SaveThreadMappingAsync(ThreadMapping threadMapping, CancellationToken cancellationToken);

    ValueTask<ApprovalRecord?> GetApprovalAsync(ApprovalId approvalId, CancellationToken cancellationToken);

    ValueTask<IReadOnlyCollection<ApprovalRecord>> GetPendingApprovalsAsync(CancellationToken cancellationToken);

    ValueTask SaveApprovalAsync(ApprovalRecord approvalRecord, CancellationToken cancellationToken);

    ValueTask<OwnerConfiguration?> GetOwnerConfigurationAsync(CancellationToken cancellationToken);
}
