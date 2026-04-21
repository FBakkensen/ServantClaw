using System.Diagnostics.CodeAnalysis;

namespace ServantClaw.Domain.Runtime;

// Simple exception type; behavior is owned by the transport adapter (T-014+).
[ExcludeFromCodeCoverage]
public sealed class BackendProtocolException : Exception
{
    public BackendProtocolException()
    {
    }

    public BackendProtocolException(string message) : base(message)
    {
    }

    public BackendProtocolException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public BackendProtocolException(string message, int errorCode)
        : base(message)
    {
        ErrorCode = errorCode;
    }

    public int? ErrorCode { get; }
}
