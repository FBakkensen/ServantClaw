using ServantClaw.Domain.Runtime;

namespace ServantClaw.Infrastructure.Runtime;

public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
