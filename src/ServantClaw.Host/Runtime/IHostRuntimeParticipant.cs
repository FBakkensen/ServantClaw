namespace ServantClaw.Host.Runtime;

public interface IHostRuntimeParticipant
{
    Task StartAsync(CancellationToken cancellationToken);

    Task StopAsync(CancellationToken cancellationToken);
}
