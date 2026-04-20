using System.Globalization;

namespace ServantClaw.Domain.Common;

public readonly record struct ChatId
{
    public ChatId(long value)
    {
        if (value == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(value), "Chat IDs must be non-zero.");
        }

        Value = value;
    }

    public long Value { get; }

    public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);
}
