namespace ServantClaw.Application.Runtime;

public interface IBackendRestartDelay
{
    Task WaitAsync(TimeSpan delay, CancellationToken cancellationToken);
}
