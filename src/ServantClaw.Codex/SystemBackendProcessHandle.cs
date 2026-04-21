using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using ServantClaw.Application.Runtime;

namespace ServantClaw.Codex;

[ExcludeFromCodeCoverage]
public sealed class SystemBackendProcessHandle : IBackendProcessHandle
{
    private readonly Process process;
    private int disposed;

    public SystemBackendProcessHandle(Process process)
    {
        this.process = process ?? throw new ArgumentNullException(nameof(process));
    }

    public int? ExitCode
    {
        get
        {
            try
            {
                return process.HasExited ? process.ExitCode : null;
            }
            catch (InvalidOperationException)
            {
                return null;
            }
        }
    }

    public Stream StandardInput => process.StandardInput.BaseStream;

    public Stream StandardOutput => process.StandardOutput.BaseStream;

    public Stream StandardError => process.StandardError.BaseStream;

    public Task WaitForExitAsync(CancellationToken cancellationToken) =>
        process.WaitForExitAsync(cancellationToken);

    public async ValueTask StopAsync(TimeSpan gracefulTimeout, CancellationToken cancellationToken)
    {
        if (process.HasExited)
        {
            return;
        }

        try
        {
            process.CloseMainWindow();
        }
        catch (InvalidOperationException)
        {
        }

        using CancellationTokenSource timeoutSource = new(gracefulTimeout);
        using CancellationTokenSource linkedSource =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutSource.Token);

        try
        {
            await process.WaitForExitAsync(linkedSource.Token);
            return;
        }
        catch (OperationCanceledException)
        {
        }

        if (!process.HasExited)
        {
            try
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(cancellationToken);
            }
            catch (InvalidOperationException)
            {
            }
        }
    }

    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) == 0)
        {
            process.Dispose();
        }

        return ValueTask.CompletedTask;
    }
}
