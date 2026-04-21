using System.Diagnostics.CodeAnalysis;

namespace ServantClaw.Domain.Runtime;

// Simple exception type; behavior is owned by the transport adapter (T-014+).
[ExcludeFromCodeCoverage]
public sealed class BackendUnavailableException : Exception
{
    public BackendUnavailableException()
    {
    }

    public BackendUnavailableException(string message) : base(message)
    {
    }

    public BackendUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
