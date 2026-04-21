using System.Diagnostics.CodeAnalysis;

namespace ServantClaw.Domain.Runtime;

// Simple exception type; behavior is owned by the transport adapter (T-014+).
[ExcludeFromCodeCoverage]
public sealed class BackendTurnFailedException : Exception
{
    public BackendTurnFailedException()
    {
    }

    public BackendTurnFailedException(string message) : base(message)
    {
    }

    public BackendTurnFailedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public BackendTurnFailedException(string message, string? turnStatus, string? codexErrorType)
        : base(message)
    {
        TurnStatus = turnStatus;
        CodexErrorType = codexErrorType;
    }

    public string? TurnStatus { get; }

    public string? CodexErrorType { get; }
}
