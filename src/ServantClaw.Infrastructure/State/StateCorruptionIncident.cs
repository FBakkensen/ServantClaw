namespace ServantClaw.Infrastructure.State;

internal sealed record StateCorruptionIncident(
    string RecordType,
    string CanonicalPath,
    string QuarantinePath,
    string Failure,
    DateTimeOffset DetectedAtUtc);
