using ServantClaw.Domain.Common;

namespace ServantClaw.Domain.Runtime;

public interface IThreadReferenceGenerator
{
    ThreadReference CreateThreadReference();
}
