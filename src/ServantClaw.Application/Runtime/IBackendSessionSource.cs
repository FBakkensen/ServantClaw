namespace ServantClaw.Application.Runtime;

public interface IBackendSessionSource
{
    BackendSession? Current { get; }

    ValueTask<BackendSession> WaitForSessionAsync(CancellationToken cancellationToken);
}
