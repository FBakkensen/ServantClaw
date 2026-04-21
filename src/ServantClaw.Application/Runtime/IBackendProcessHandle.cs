namespace ServantClaw.Application.Runtime;

public interface IBackendProcessHandle : IAsyncDisposable
{
    int? ExitCode { get; }

    Task WaitForExitAsync(CancellationToken cancellationToken);

    ValueTask StopAsync(TimeSpan gracefulTimeout, CancellationToken cancellationToken);
}
