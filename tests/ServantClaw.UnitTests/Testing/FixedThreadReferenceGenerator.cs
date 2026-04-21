using ServantClaw.Domain.Common;
using ServantClaw.Domain.Runtime;

namespace ServantClaw.UnitTests.Testing;

internal sealed class FixedThreadReferenceGenerator(IEnumerable<string> threadValues) : IThreadReferenceGenerator
{
    private readonly Queue<string> threadValues = new(threadValues ?? throw new ArgumentNullException(nameof(threadValues)));

    public ThreadReference CreateThreadReference()
    {
        if (threadValues.Count == 0)
        {
            throw new InvalidOperationException("No more thread references are configured for this test.");
        }

        return new ThreadReference(threadValues.Dequeue());
    }
}
