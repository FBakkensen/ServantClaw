namespace ServantClaw.Domain.Runtime;

public interface IProcessSupervisor
{
    ValueTask StartAsync(CancellationToken cancellationToken);

    ValueTask StopAsync(CancellationToken cancellationToken);

    ValueTask<bool> IsHealthyAsync(CancellationToken cancellationToken);
}
