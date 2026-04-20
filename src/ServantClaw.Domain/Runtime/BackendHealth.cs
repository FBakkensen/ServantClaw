namespace ServantClaw.Domain.Runtime;

public sealed record BackendHealth(bool IsReady, string? Detail = null);
