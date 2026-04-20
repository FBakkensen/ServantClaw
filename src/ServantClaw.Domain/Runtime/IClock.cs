namespace ServantClaw.Domain.Runtime;

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}
