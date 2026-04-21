namespace ServantClaw.Application.Approvals;

public enum ApprovalResolutionOutcome
{
    Resolved = 1,
    UnknownId = 2,
    AlreadyResolved = 3,
    WrongChat = 4,
    NotActive = 5
}

public sealed record ApprovalResolutionResult(ApprovalResolutionOutcome Outcome, string Message)
{
    public string Message { get; } = string.IsNullOrWhiteSpace(Message)
        ? throw new ArgumentException("Message must be provided.", nameof(Message))
        : Message.Trim();
}
