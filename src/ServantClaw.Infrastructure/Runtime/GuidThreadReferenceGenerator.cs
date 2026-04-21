using ServantClaw.Domain.Common;
using ServantClaw.Domain.Runtime;

namespace ServantClaw.Infrastructure.Runtime;

public sealed class GuidThreadReferenceGenerator : IThreadReferenceGenerator
{
    public ThreadReference CreateThreadReference() =>
        new($"thread-{Guid.NewGuid():N}");
}
