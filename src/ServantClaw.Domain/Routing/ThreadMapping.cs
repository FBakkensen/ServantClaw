using ServantClaw.Domain.Common;

namespace ServantClaw.Domain.Routing;

public sealed record ThreadMapping
{
    public ThreadMapping(ThreadContext Context, ThreadReference? CurrentThread, IReadOnlyList<ThreadReference>? PreviousThreads = null)
    {
        ArgumentNullException.ThrowIfNull(Context);
        this.Context = Context;
        this.CurrentThread = CurrentThread;
        this.PreviousThreads = PreviousThreads is null ? [] : [.. PreviousThreads];
    }

    public ThreadContext Context { get; }

    public ThreadReference? CurrentThread { get; }

    public IReadOnlyList<ThreadReference> PreviousThreads { get; }

    public ThreadMapping Rotate()
    {
        if (CurrentThread is null)
        {
            return this;
        }

        List<ThreadReference> history = [CurrentThread.Value, .. PreviousThreads];
        return new ThreadMapping(Context, null, history);
    }

    public ThreadMapping WithCurrentThread(ThreadReference newCurrentThread) =>
        new(Context, newCurrentThread, PreviousThreads);
}
