using ServantClaw.Domain.Routing;
using ServantClaw.Domain.State;

namespace ServantClaw.Application.Runtime;

public sealed class ThreadMappingCoordinator(IStateStore stateStore)
{
    private readonly IStateStore stateStore = stateStore ?? throw new ArgumentNullException(nameof(stateStore));

    public async ValueTask<ThreadMapping> ResolveAsync(ThreadContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        ThreadMapping? existingMapping = await stateStore.GetThreadMappingAsync(context, cancellationToken);
        if (existingMapping is not null)
        {
            return existingMapping;
        }

        ThreadMapping createdMapping = new(context, null);
        await stateStore.SaveThreadMappingAsync(createdMapping, cancellationToken);
        return createdMapping;
    }

    public async ValueTask<ThreadMapping> RotateAsync(ThreadContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        ThreadMapping? existingMapping = await stateStore.GetThreadMappingAsync(context, cancellationToken);
        ThreadMapping updatedMapping = existingMapping is null
            ? new ThreadMapping(context, null)
            : existingMapping.Rotate();

        await stateStore.SaveThreadMappingAsync(updatedMapping, cancellationToken);
        return updatedMapping;
    }
}
