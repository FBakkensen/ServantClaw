using ServantClaw.Domain.Common;

namespace ServantClaw.Domain.Routing;

public sealed record ThreadMapping
{
    public ThreadMapping(ThreadContext Context, ThreadReference CurrentThread, IReadOnlyList<ThreadReference>? PreviousThreads = null)
    {
        this.Context = Context;
        this.CurrentThread = CurrentThread;
        this.PreviousThreads = PreviousThreads is null ? [] : [.. PreviousThreads];
    }

    public ThreadContext Context { get; }

    public ThreadReference CurrentThread { get; }

    public IReadOnlyList<ThreadReference> PreviousThreads { get; }

    public ThreadMapping Rotate(ThreadReference newCurrentThread)
    {
        List<ThreadReference> history = [CurrentThread, .. PreviousThreads];
        return new ThreadMapping(Context, newCurrentThread, history);
    }
}
