using System.Diagnostics.CodeAnalysis;
using ServantClaw.Application.Runtime;

namespace ServantClaw.Codex;

[ExcludeFromCodeCoverage]
public sealed class SystemBackendRestartDelay : IBackendRestartDelay
{
    public Task WaitAsync(TimeSpan delay, CancellationToken cancellationToken) =>
        Task.Delay(delay, cancellationToken);
}
