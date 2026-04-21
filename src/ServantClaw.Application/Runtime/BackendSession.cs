using System.Diagnostics.CodeAnalysis;

namespace ServantClaw.Application.Runtime;

// Plain data record carrying live IO streams + a per-process lifetime token. No branching.
[ExcludeFromCodeCoverage]
public sealed class BackendSession
{
    public BackendSession(
        Stream standardInput,
        Stream standardOutput,
        Stream standardError,
        CancellationToken sessionLifetime)
    {
        StandardInput = standardInput ?? throw new ArgumentNullException(nameof(standardInput));
        StandardOutput = standardOutput ?? throw new ArgumentNullException(nameof(standardOutput));
        StandardError = standardError ?? throw new ArgumentNullException(nameof(standardError));
        SessionLifetime = sessionLifetime;
    }

    public Stream StandardInput { get; }

    public Stream StandardOutput { get; }

    public Stream StandardError { get; }

    public CancellationToken SessionLifetime { get; }
}
