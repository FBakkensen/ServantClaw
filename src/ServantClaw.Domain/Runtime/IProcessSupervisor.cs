namespace ServantClaw.Domain.Runtime;

public interface IProcessSupervisor
{
    Task StartAsync(CancellationToken cancellationToken);

    Task StopAsync(CancellationToken cancellationToken);

    ValueTask<BackendHealth> GetHealthAsync(CancellationToken cancellationToken);
}
