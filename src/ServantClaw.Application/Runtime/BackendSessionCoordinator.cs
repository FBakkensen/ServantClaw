namespace ServantClaw.Application.Runtime;

public sealed class BackendSessionCoordinator : IBackendSessionPublisher, IBackendSessionSource
{
    private readonly Lock gate = new();
    private BackendSession? current;
    private TaskCompletionSource<BackendSession> ready = CreateReady();

    public BackendSession? Current
    {
        get
        {
            lock (gate)
            {
                return current;
            }
        }
    }

    public void Publish(BackendSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        TaskCompletionSource<BackendSession> toComplete;
        lock (gate)
        {
            current = session;
            if (ready.Task.IsCompleted)
            {
                ready = CreateReady();
            }

            toComplete = ready;
        }

        toComplete.TrySetResult(session);
    }

    public void Retract()
    {
        lock (gate)
        {
            current = null;
            if (ready.Task.IsCompleted)
            {
                ready = CreateReady();
            }
        }
    }

    public async ValueTask<BackendSession> WaitForSessionAsync(CancellationToken cancellationToken)
    {
        Task<BackendSession> task;
        lock (gate)
        {
            if (current is not null)
            {
                return current;
            }

            task = ready.Task;
        }

        return await task.WaitAsync(cancellationToken);
    }

    private static TaskCompletionSource<BackendSession> CreateReady() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);
}
