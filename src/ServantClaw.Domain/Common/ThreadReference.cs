namespace ServantClaw.Domain.Common;

public readonly record struct ThreadReference
{
    public ThreadReference(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value.Trim();
    }

    public string Value { get; }

    public override string ToString() => Value;
}
