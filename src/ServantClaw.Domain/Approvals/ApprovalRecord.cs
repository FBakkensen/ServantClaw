using System.Collections.ObjectModel;
using ServantClaw.Domain.Common;

namespace ServantClaw.Domain.Approvals;

public sealed record ApprovalRecord
{
    private static readonly ReadOnlyDictionary<string, string> EmptyMetadata =
        new(new Dictionary<string, string>(0, StringComparer.Ordinal));

    public ApprovalRecord(
        ApprovalId approvalId,
        ApprovalClass approvalClass,
        ApprovalContext context,
        string summary,
        DateTimeOffset createdAt,
        IReadOnlyDictionary<string, string>? operationMetadata = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(summary);

        ApprovalId = approvalId;
        ApprovalClass = approvalClass;
        Context = context ?? throw new ArgumentNullException(nameof(context));
        Summary = summary.Trim();
        CreatedAt = createdAt;
        OperationMetadata = CloneMetadata(operationMetadata);
    }

    public ApprovalId ApprovalId { get; }

    public ApprovalClass ApprovalClass { get; }

    public ApprovalContext Context { get; }

    public string Summary { get; }

    public DateTimeOffset CreatedAt { get; }

    public DateTimeOffset? ResolvedAt { get; init; }

    public ApprovalDecision? Decision { get; init; }

    public IReadOnlyDictionary<string, string> OperationMetadata { get; }

    public bool IsPending => ResolvedAt is null && Decision is null;

    public ApprovalRecord Resolve(ApprovalDecision decision, DateTimeOffset resolvedAt)
    {
        if (!IsPending)
        {
            throw new InvalidOperationException("Approval has already been resolved.");
        }

        if (resolvedAt < CreatedAt)
        {
            throw new ArgumentOutOfRangeException(nameof(resolvedAt), "Resolution time must be on or after the creation time.");
        }

        return this with
        {
            Decision = decision,
            ResolvedAt = resolvedAt
        };
    }

    private static ReadOnlyDictionary<string, string> CloneMetadata(IReadOnlyDictionary<string, string>? metadata)
    {
        if (metadata is null || metadata.Count == 0)
        {
            return EmptyMetadata;
        }

        Dictionary<string, string> copy = new(StringComparer.Ordinal);
        foreach ((string key, string value) in metadata)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(key);
            ArgumentNullException.ThrowIfNull(value);
            copy[key] = value;
        }

        return new ReadOnlyDictionary<string, string>(copy);
    }
}
