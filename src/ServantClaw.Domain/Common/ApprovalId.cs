namespace ServantClaw.Domain.Common;

public readonly record struct ApprovalId
{
    public ApprovalId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value.Trim();
    }

    public string Value { get; }

    public override string ToString() => Value;
}
