namespace ServantClaw.Application.Runtime;

public interface IBackendProcessHandle : IAsyncDisposable
{
    int? ExitCode { get; }

    Stream StandardInput { get; }

    Stream StandardOutput { get; }

    Stream StandardError { get; }

    Task WaitForExitAsync(CancellationToken cancellationToken);

    ValueTask StopAsync(TimeSpan gracefulTimeout, CancellationToken cancellationToken);
}
