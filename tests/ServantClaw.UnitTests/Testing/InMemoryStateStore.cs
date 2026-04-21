using ServantClaw.Domain.Approvals;
using ServantClaw.Domain.Common;
using ServantClaw.Domain.Configuration;
using ServantClaw.Domain.Routing;
using ServantClaw.Domain.State;

namespace ServantClaw.UnitTests.Testing;

internal sealed class InMemoryStateStore : IStateStore
{
    public Dictionary<long, ChatState> ChatStates { get; } = [];

    public Dictionary<ThreadContext, ThreadMapping> ThreadMappings { get; } = [];

    public Dictionary<ApprovalId, ApprovalRecord> Approvals { get; } = [];

    public ValueTask<ChatState?> GetChatStateAsync(ChatId chatId, CancellationToken cancellationToken) =>
        ValueTask.FromResult(ChatStates.TryGetValue(chatId.Value, out ChatState? state) ? state : null);

    public ValueTask SaveChatStateAsync(ChatState chatState, CancellationToken cancellationToken)
    {
        ChatStates[chatState.ChatId.Value] = chatState;
        return ValueTask.CompletedTask;
    }

    public ValueTask<ThreadMapping?> GetThreadMappingAsync(ThreadContext context, CancellationToken cancellationToken) =>
        ValueTask.FromResult(ThreadMappings.TryGetValue(context, out ThreadMapping? mapping) ? mapping : null);

    public ValueTask SaveThreadMappingAsync(ThreadMapping threadMapping, CancellationToken cancellationToken)
    {
        ThreadMappings[threadMapping.Context] = threadMapping;
        return ValueTask.CompletedTask;
    }

    public ValueTask<ApprovalRecord?> GetApprovalAsync(ApprovalId approvalId, CancellationToken cancellationToken) =>
        ValueTask.FromResult(Approvals.TryGetValue(approvalId, out ApprovalRecord? record) ? record : null);

    public ValueTask<IReadOnlyCollection<ApprovalRecord>> GetPendingApprovalsAsync(CancellationToken cancellationToken) =>
        ValueTask.FromResult<IReadOnlyCollection<ApprovalRecord>>(
            Approvals.Values.Where(record => record.IsPending).ToArray());

    public ValueTask SaveApprovalAsync(ApprovalRecord approvalRecord, CancellationToken cancellationToken)
    {
        Approvals[approvalRecord.ApprovalId] = approvalRecord;
        return ValueTask.CompletedTask;
    }

    public ValueTask<OwnerConfiguration?> GetOwnerConfigurationAsync(CancellationToken cancellationToken) =>
        ValueTask.FromResult<OwnerConfiguration?>(null);
}
