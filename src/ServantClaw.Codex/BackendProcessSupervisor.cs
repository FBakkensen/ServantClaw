using Microsoft.Extensions.Logging;
using ServantClaw.Application.Runtime;
using ServantClaw.Domain.Configuration;
using ServantClaw.Domain.Runtime;

namespace ServantClaw.Codex;

public sealed partial class BackendProcessSupervisor :
    IProcessSupervisor, IHostRuntimeParticipant, IAsyncDisposable
{
    internal static readonly IReadOnlyList<TimeSpan> BackoffSchedule =
    [
        TimeSpan.FromSeconds(1),
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(10),
    ];

    internal static readonly TimeSpan BackoffCap = TimeSpan.FromSeconds(30);

    internal static readonly TimeSpan HealthyResetWindow = TimeSpan.FromSeconds(60);

    internal static readonly TimeSpan GracefulStopTimeout = TimeSpan.FromSeconds(5);

    private static readonly BackendHealth NotStartedHealth = new(false, "not started");
    private static readonly BackendHealth RunningHealth = new(true, "running");
    private static readonly BackendHealth StoppedHealth = new(false, "stopped");

    private readonly BackendConfiguration configuration;
    private readonly IBackendProcessLauncher launcher;
    private readonly IBackendRestartDelay restartDelay;
    private readonly IClock clock;
    private readonly IBackendSessionPublisher sessionPublisher;
    private readonly ILogger<BackendProcessSupervisor> logger;

    private readonly Lock gate = new();

    private CancellationTokenSource? shutdownSource;
    private Task? supervisionTask;
    private BackendHealth currentHealth = NotStartedHealth;

    public BackendProcessSupervisor(
        BackendConfiguration configuration,
        IBackendProcessLauncher launcher,
        IBackendRestartDelay restartDelay,
        IClock clock,
        IBackendSessionPublisher sessionPublisher,
        ILogger<BackendProcessSupervisor> logger)
    {
        this.configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        this.launcher = launcher ?? throw new ArgumentNullException(nameof(launcher));
        this.restartDelay = restartDelay ?? throw new ArgumentNullException(nameof(restartDelay));
        this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
        this.sessionPublisher = sessionPublisher ?? throw new ArgumentNullException(nameof(sessionPublisher));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        CancellationToken shutdownToken;
        lock (gate)
        {
            if (shutdownSource is not null)
            {
                throw new InvalidOperationException("Supervisor already started.");
            }

            shutdownSource = new CancellationTokenSource();
            shutdownToken = shutdownSource.Token;
            currentHealth = new BackendHealth(false, "starting");
        }

        Log.SupervisorStarted(logger);
        supervisionTask = Task.Run(() => RunLoopAsync(shutdownToken), CancellationToken.None);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        CancellationTokenSource? source;
        Task? task;

        lock (gate)
        {
            source = shutdownSource;
            shutdownSource = null;
            task = supervisionTask;
            supervisionTask = null;
        }

        if (source is null)
        {
            return;
        }

        Log.SupervisorStopping(logger);
        await source.CancelAsync();

        if (task is not null)
        {
            try
            {
                await task.WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
        }

        source.Dispose();

        lock (gate)
        {
            currentHealth = StoppedHealth;
        }

        Log.SupervisorStopped(logger);
    }

    public ValueTask<BackendHealth> GetHealthAsync(CancellationToken cancellationToken)
    {
        lock (gate)
        {
            return new ValueTask<BackendHealth>(currentHealth);
        }
    }

    public ValueTask DisposeAsync() => new(StopAsync(CancellationToken.None));

    private async Task RunLoopAsync(CancellationToken shutdownToken)
    {
        int restartIndex = 0;

        try
        {
            while (true)
            {
                shutdownToken.ThrowIfCancellationRequested();

                IBackendProcessHandle? handle = null;
                CancellationTokenSource? sessionSource = null;
                bool sessionPublished = false;
                DateTimeOffset startTime = clock.UtcNow;

                try
                {
                    handle = launcher.Launch(configuration);
                    sessionSource = new CancellationTokenSource();
                    BackendSession session = new(
                        handle.StandardInput,
                        handle.StandardOutput,
                        handle.StandardError,
                        sessionSource.Token);
                    sessionPublisher.Publish(session);
                    sessionPublished = true;
                    SetHealth(RunningHealth);
                    Log.BackendStarted(logger);

                    await handle.WaitForExitAsync(shutdownToken);
                    Log.BackendExited(logger, handle.ExitCode);
                }
                catch (OperationCanceledException) when (shutdownToken.IsCancellationRequested)
                {
                    if (handle is not null)
                    {
                        try
                        {
                            await handle.StopAsync(GracefulStopTimeout, CancellationToken.None);
                        }
                        catch (Exception stopException)
                        {
                            Log.BackendStopFailed(logger, stopException);
                        }
                    }

                    throw;
                }
                catch (Exception launchException)
                {
                    Log.BackendLaunchFailed(logger, launchException);
                }
                finally
                {
                    if (sessionPublished)
                    {
                        sessionPublisher.Retract();
                    }

                    if (sessionSource is not null)
                    {
                        try
                        {
                            await sessionSource.CancelAsync();
                        }
                        catch (ObjectDisposedException)
                        {
                        }

                        sessionSource.Dispose();
                    }

                    if (handle is not null)
                    {
                        try
                        {
                            await handle.DisposeAsync();
                        }
                        catch (Exception disposeException)
                        {
                            Log.BackendDisposeFailed(logger, disposeException);
                        }
                    }
                }

                TimeSpan runtime = clock.UtcNow - startTime;
                if (runtime >= HealthyResetWindow)
                {
                    restartIndex = 0;
                }

                TimeSpan delay = GetDelay(restartIndex);
                restartIndex++;
                SetHealth(new BackendHealth(false, "restarting"));
                Log.BackendRestartScheduled(logger, (long)delay.TotalMilliseconds);

                await restartDelay.WaitAsync(delay, shutdownToken);
            }
        }
        catch (OperationCanceledException) when (shutdownToken.IsCancellationRequested)
        {
        }
    }

    private static TimeSpan GetDelay(int index) =>
        index < BackoffSchedule.Count ? BackoffSchedule[index] : BackoffCap;

    private void SetHealth(BackendHealth health)
    {
        lock (gate)
        {
            currentHealth = health;
        }
    }

    private static partial class Log
    {
        [LoggerMessage(EventId = 500, Level = LogLevel.Information, Message = "Backend supervisor started")]
        public static partial void SupervisorStarted(ILogger logger);

        [LoggerMessage(EventId = 501, Level = LogLevel.Information, Message = "Backend supervisor stopping")]
        public static partial void SupervisorStopping(ILogger logger);

        [LoggerMessage(EventId = 502, Level = LogLevel.Information, Message = "Backend supervisor stopped")]
        public static partial void SupervisorStopped(ILogger logger);

        [LoggerMessage(EventId = 503, Level = LogLevel.Information, Message = "Backend process started")]
        public static partial void BackendStarted(ILogger logger);

        [LoggerMessage(EventId = 504, Level = LogLevel.Warning, Message = "Backend process exited with code {ExitCode}")]
        public static partial void BackendExited(ILogger logger, int? exitCode);

        [LoggerMessage(EventId = 505, Level = LogLevel.Critical, Message = "Backend process launch failed")]
        public static partial void BackendLaunchFailed(ILogger logger, Exception exception);

        [LoggerMessage(EventId = 506, Level = LogLevel.Information, Message = "Backend restart scheduled in {DelayMilliseconds}ms")]
        public static partial void BackendRestartScheduled(ILogger logger, long delayMilliseconds);

        [LoggerMessage(EventId = 507, Level = LogLevel.Warning, Message = "Backend process graceful stop failed")]
        public static partial void BackendStopFailed(ILogger logger, Exception exception);

        [LoggerMessage(EventId = 508, Level = LogLevel.Warning, Message = "Backend process handle disposal failed")]
        public static partial void BackendDisposeFailed(ILogger logger, Exception exception);
    }
}
