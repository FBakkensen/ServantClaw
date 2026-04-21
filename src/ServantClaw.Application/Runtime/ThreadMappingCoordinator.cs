using ServantClaw.Domain.Routing;
using ServantClaw.Domain.Runtime;
using ServantClaw.Domain.State;

namespace ServantClaw.Application.Runtime;

public sealed class ThreadMappingCoordinator(IStateStore stateStore, IThreadReferenceGenerator threadReferenceGenerator)
{
    private readonly IStateStore stateStore = stateStore ?? throw new ArgumentNullException(nameof(stateStore));
    private readonly IThreadReferenceGenerator threadReferenceGenerator = threadReferenceGenerator ?? throw new ArgumentNullException(nameof(threadReferenceGenerator));

    public async ValueTask<ThreadMapping> ResolveAsync(ThreadContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        ThreadMapping? existingMapping = await stateStore.GetThreadMappingAsync(context, cancellationToken);
        if (existingMapping is not null)
        {
            return existingMapping;
        }

        ThreadMapping createdMapping = CreateInitialMapping(context);
        await stateStore.SaveThreadMappingAsync(createdMapping, cancellationToken);
        return createdMapping;
    }

    public async ValueTask<ThreadMapping> RotateAsync(ThreadContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        ThreadMapping? existingMapping = await stateStore.GetThreadMappingAsync(context, cancellationToken);
        ThreadMapping updatedMapping = existingMapping is null
            ? CreateInitialMapping(context)
            : existingMapping.Rotate(threadReferenceGenerator.CreateThreadReference());

        await stateStore.SaveThreadMappingAsync(updatedMapping, cancellationToken);
        return updatedMapping;
    }

    private ThreadMapping CreateInitialMapping(ThreadContext context) =>
        new(context, threadReferenceGenerator.CreateThreadReference());
}
