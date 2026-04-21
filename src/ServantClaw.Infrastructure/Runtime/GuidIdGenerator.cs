using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using ServantClaw.Domain.Common;
using ServantClaw.Domain.Runtime;

namespace ServantClaw.Infrastructure.Runtime;

// Trivial GUID-based ID source. Behaviour is owned by the future approvals task (T-016).
[ExcludeFromCodeCoverage]
public sealed class GuidIdGenerator : IIdGenerator
{
    public ApprovalId CreateApprovalId() =>
        new(Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture));
}
