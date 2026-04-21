using System.Threading.Channels;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using ServantClaw.Application.Runtime;
using ServantClaw.Codex;
using ServantClaw.Domain.Configuration;
using ServantClaw.Domain.Runtime;
using Xunit;

namespace ServantClaw.UnitTests.Runtime;

public sealed class BackendProcessSupervisorTests
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(5);

    private static BackendConfiguration CreateConfig() =>
        new("codex", "C:\\ServantClaw", ["app-server"]);

    private static BackendProcessSupervisor CreateSupervisor(
        IBackendProcessLauncher launcher,
        IBackendRestartDelay delay,
        IClock clock,
        ILogger<BackendProcessSupervisor>? logger = null) =>
        new(
            CreateConfig(),
            launcher,
            delay,
            clock,
            logger ?? NullLogger<BackendProcessSupervisor>.Instance);

    [Fact]
    public async Task StartAsyncShouldLaunchBackendAndReportHealthy()
    {
        FakeBackendProcessLauncher launcher = new();
        FakeBackendRestartDelay delay = new();
        FakeClock clock = new();
        FakeBackendProcessHandle handle = new();
        launcher.EnqueueHandle(handle);

        BackendProcessSupervisor supervisor = CreateSupervisor(launcher, delay, clock);

        await supervisor.StartAsync(CancellationToken.None);
        await launcher.WaitForLaunchCount(1, TestTimeout);
        await WaitForHealth(supervisor, h => h.IsReady, TestTimeout);

        launcher.LaunchedConfigurations.Should().HaveCount(1);
        launcher.LaunchedConfigurations[0].ExecutablePath.Should().Be("codex");
        (await supervisor.GetHealthAsync(CancellationToken.None)).IsReady.Should().BeTrue();
        delay.Delays.Should().BeEmpty();

        handle.SignalExit(0);
        await supervisor.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task LauncherThrowingOnFirstStartShouldBeNonFatalAndTriggerRestart()
    {
        FakeBackendProcessLauncher launcher = new();
        FakeBackendRestartDelay delay = new();
        FakeClock clock = new();
        FakeBackendProcessHandle secondHandle = new();

        launcher.EnqueueException(new InvalidOperationException("executable missing"));
        launcher.EnqueueHandle(secondHandle);

        BackendProcessSupervisor supervisor = CreateSupervisor(launcher, delay, clock);

        Func<Task> start = () => supervisor.StartAsync(CancellationToken.None);
        await start.Should().NotThrowAsync();

        await launcher.WaitForLaunchCount(2, TestTimeout);
        await WaitForHealth(supervisor, h => h.IsReady, TestTimeout);

        delay.Delays.Should().ContainSingle().Which.Should().Be(TimeSpan.FromSeconds(1));

        secondHandle.SignalExit(0);
        await supervisor.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task UnexpectedExitShouldTriggerRestartAfterBackoff()
    {
        FakeBackendProcessLauncher launcher = new();
        FakeBackendRestartDelay delay = new();
        FakeClock clock = new();
        FakeBackendProcessHandle first = new();
        FakeBackendProcessHandle second = new();
        launcher.EnqueueHandle(first);
        launcher.EnqueueHandle(second);

        BackendProcessSupervisor supervisor = CreateSupervisor(launcher, delay, clock);

        await supervisor.StartAsync(CancellationToken.None);
        await launcher.WaitForLaunchCount(1, TestTimeout);

        first.SignalExit(137);

        await launcher.WaitForLaunchCount(2, TestTimeout);
        delay.Delays.Should().ContainSingle().Which.Should().Be(TimeSpan.FromSeconds(1));

        second.SignalExit(0);
        await supervisor.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task BackoffProgressionShouldFollowOneTwoFiveTenCappedAtThirty()
    {
        FakeBackendProcessLauncher launcher = new();
        FakeBackendRestartDelay delay = new();
        FakeClock clock = new();

        for (int attempt = 0; attempt < 7; attempt++)
        {
            launcher.EnqueueHandle(new AutoExitFakeHandle(exitCode: 1));
        }

        BackendProcessSupervisor supervisor = CreateSupervisor(launcher, delay, clock);

        await supervisor.StartAsync(CancellationToken.None);
        await launcher.WaitForLaunchCount(7, TestTimeout);
        await delay.WaitForDelayCount(6, TestTimeout);

        delay.Delays.Take(6).Should().Equal(
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(2),
            TimeSpan.FromSeconds(5),
            TimeSpan.FromSeconds(10),
            TimeSpan.FromSeconds(30),
            TimeSpan.FromSeconds(30));

        await supervisor.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task BackoffShouldResetAfterHealthyRuntimeWindow()
    {
        FakeBackendProcessLauncher launcher = new();
        FakeBackendRestartDelay delay = new();
        FakeClock clock = new(DateTimeOffset.UtcNow);
        FakeBackendProcessHandle first = new();
        FakeBackendProcessHandle second = new();
        FakeBackendProcessHandle third = new();
        launcher.EnqueueHandle(first);
        launcher.EnqueueHandle(second);
        launcher.EnqueueHandle(third);

        BackendProcessSupervisor supervisor = CreateSupervisor(launcher, delay, clock);

        await supervisor.StartAsync(CancellationToken.None);
        await launcher.WaitForLaunchCount(1, TestTimeout);

        clock.Advance(TimeSpan.FromSeconds(2));
        first.SignalExit(1);

        await launcher.WaitForLaunchCount(2, TestTimeout);

        clock.Advance(TimeSpan.FromSeconds(90));
        second.SignalExit(1);

        await launcher.WaitForLaunchCount(3, TestTimeout);

        clock.Advance(TimeSpan.FromSeconds(1));
        third.SignalExit(1);

        await delay.WaitForDelayCount(3, TestTimeout);

        delay.Delays[0].Should().Be(TimeSpan.FromSeconds(1));
        delay.Delays[1].Should().Be(TimeSpan.FromSeconds(1));
        delay.Delays[2].Should().Be(TimeSpan.FromSeconds(2));

        await supervisor.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task StopAsyncShouldGracefullyStopCurrentHandleAndHaltRestarts()
    {
        FakeBackendProcessLauncher launcher = new();
        FakeBackendRestartDelay delay = new();
        FakeClock clock = new();
        FakeBackendProcessHandle handle = new();
        FakeBackendProcessHandle unexpected = new();
        launcher.EnqueueHandle(handle);
        launcher.EnqueueHandle(unexpected);

        BackendProcessSupervisor supervisor = CreateSupervisor(launcher, delay, clock);

        await supervisor.StartAsync(CancellationToken.None);
        await launcher.WaitForLaunchCount(1, TestTimeout);

        await supervisor.StopAsync(CancellationToken.None);

        handle.StopCalls.Should().Be(1);
        handle.LastGracefulTimeout.Should().Be(TimeSpan.FromSeconds(5));
        handle.Disposed.Should().BeTrue();
        launcher.LaunchCount.Should().Be(1);
        unexpected.StopCalls.Should().Be(0);
        (await supervisor.GetHealthAsync(CancellationToken.None)).IsReady.Should().BeFalse();
    }

    [Fact]
    public async Task StopAsyncDuringBackoffShouldHaltWithoutAnotherLaunch()
    {
        FakeBackendProcessLauncher launcher = new();
        BlockingFakeBackendRestartDelay delay = new();
        FakeClock clock = new();
        FakeBackendProcessHandle first = new();
        FakeBackendProcessHandle unused = new();
        launcher.EnqueueHandle(first);
        launcher.EnqueueHandle(unused);

        BackendProcessSupervisor supervisor = CreateSupervisor(launcher, delay, clock);

        await supervisor.StartAsync(CancellationToken.None);
        await launcher.WaitForLaunchCount(1, TestTimeout);

        first.SignalExit(1);
        await delay.WaitForFirstInvocation(TestTimeout);

        await supervisor.StopAsync(CancellationToken.None);

        launcher.LaunchCount.Should().Be(1);
        unused.StopCalls.Should().Be(0);
    }

    [Fact]
    public async Task GetHealthAsyncShouldReportNotStartedBeforeStart()
    {
        FakeBackendProcessLauncher launcher = new();
        FakeBackendRestartDelay delay = new();
        FakeClock clock = new();

        BackendProcessSupervisor supervisor = CreateSupervisor(launcher, delay, clock);

        BackendHealth health = await supervisor.GetHealthAsync(CancellationToken.None);
        health.IsReady.Should().BeFalse();
        health.Detail.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task GetHealthAsyncShouldReportRestartingAfterExit()
    {
        FakeBackendProcessLauncher launcher = new();
        BlockingFakeBackendRestartDelay delay = new();
        FakeClock clock = new();
        FakeBackendProcessHandle first = new();
        FakeBackendProcessHandle second = new();
        launcher.EnqueueHandle(first);
        launcher.EnqueueHandle(second);

        BackendProcessSupervisor supervisor = CreateSupervisor(launcher, delay, clock);

        await supervisor.StartAsync(CancellationToken.None);
        await launcher.WaitForLaunchCount(1, TestTimeout);

        first.SignalExit(1);
        await delay.WaitForFirstInvocation(TestTimeout);
        await WaitForHealth(supervisor, h => !h.IsReady, TestTimeout);

        (await supervisor.GetHealthAsync(CancellationToken.None)).IsReady.Should().BeFalse();

        delay.Release();
        await launcher.WaitForLaunchCount(2, TestTimeout);

        second.SignalExit(0);
        await supervisor.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task StartAsyncCalledTwiceShouldThrow()
    {
        FakeBackendProcessLauncher launcher = new();
        FakeBackendRestartDelay delay = new();
        FakeClock clock = new();
        FakeBackendProcessHandle handle = new();
        launcher.EnqueueHandle(handle);

        BackendProcessSupervisor supervisor = CreateSupervisor(launcher, delay, clock);

        await supervisor.StartAsync(CancellationToken.None);

        Func<Task> act = () => supervisor.StartAsync(CancellationToken.None);
        await act.Should().ThrowAsync<InvalidOperationException>();

        handle.SignalExit(0);
        await supervisor.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task StopAsyncWithoutStartShouldBeNoOp()
    {
        FakeBackendProcessLauncher launcher = new();
        FakeBackendRestartDelay delay = new();
        FakeClock clock = new();

        BackendProcessSupervisor supervisor = CreateSupervisor(launcher, delay, clock);

        Func<Task> act = () => supervisor.StopAsync(CancellationToken.None);
        await act.Should().NotThrowAsync();

        launcher.LaunchCount.Should().Be(0);
    }

    [Fact]
    public async Task StartAsyncAfterStopAsyncShouldSucceed()
    {
        FakeBackendProcessLauncher launcher = new();
        FakeBackendRestartDelay delay = new();
        FakeClock clock = new();
        FakeBackendProcessHandle first = new();
        FakeBackendProcessHandle second = new();
        launcher.EnqueueHandle(first);
        launcher.EnqueueHandle(second);

        BackendProcessSupervisor supervisor = CreateSupervisor(launcher, delay, clock);

        await supervisor.StartAsync(CancellationToken.None);
        await launcher.WaitForLaunchCount(1, TestTimeout);
        await supervisor.StopAsync(CancellationToken.None);

        await supervisor.StartAsync(CancellationToken.None);
        await launcher.WaitForLaunchCount(2, TestTimeout);

        second.SignalExit(0);
        await supervisor.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task LauncherFailureShouldLogAtCriticalLevel()
    {
        ILogger<BackendProcessSupervisor> logger = Substitute.For<ILogger<BackendProcessSupervisor>>();
        logger.IsEnabled(Arg.Any<LogLevel>()).Returns(true);

        FakeBackendProcessLauncher launcher = new();
        FakeBackendRestartDelay delay = new();
        FakeClock clock = new();
        FakeBackendProcessHandle recoveryHandle = new();
        launcher.EnqueueException(new InvalidOperationException("boom"));
        launcher.EnqueueHandle(recoveryHandle);

        BackendProcessSupervisor supervisor = CreateSupervisor(launcher, delay, clock, logger);

        await supervisor.StartAsync(CancellationToken.None);
        await launcher.WaitForLaunchCount(2, TestTimeout);

        logger.Received().Log(
            LogLevel.Critical,
            Arg.Any<EventId>(),
            Arg.Any<object>(),
            Arg.Any<Exception?>(),
            Arg.Any<Func<object, Exception?, string>>());

        recoveryHandle.SignalExit(0);
        await supervisor.StopAsync(CancellationToken.None);
    }

    [Fact]
    public void ConstructorShouldRejectNullConfiguration()
    {
        Action act = () => _ = new BackendProcessSupervisor(
            null!,
            new FakeBackendProcessLauncher(),
            new FakeBackendRestartDelay(),
            new FakeClock(),
            NullLogger<BackendProcessSupervisor>.Instance);

        act.Should().Throw<ArgumentNullException>().Which.ParamName.Should().Be("configuration");
    }

    [Fact]
    public void ConstructorShouldRejectNullLauncher()
    {
        Action act = () => _ = new BackendProcessSupervisor(
            CreateConfig(),
            null!,
            new FakeBackendRestartDelay(),
            new FakeClock(),
            NullLogger<BackendProcessSupervisor>.Instance);

        act.Should().Throw<ArgumentNullException>().Which.ParamName.Should().Be("launcher");
    }

    [Fact]
    public void ConstructorShouldRejectNullRestartDelay()
    {
        Action act = () => _ = new BackendProcessSupervisor(
            CreateConfig(),
            new FakeBackendProcessLauncher(),
            null!,
            new FakeClock(),
            NullLogger<BackendProcessSupervisor>.Instance);

        act.Should().Throw<ArgumentNullException>().Which.ParamName.Should().Be("restartDelay");
    }

    [Fact]
    public void ConstructorShouldRejectNullClock()
    {
        Action act = () => _ = new BackendProcessSupervisor(
            CreateConfig(),
            new FakeBackendProcessLauncher(),
            new FakeBackendRestartDelay(),
            null!,
            NullLogger<BackendProcessSupervisor>.Instance);

        act.Should().Throw<ArgumentNullException>().Which.ParamName.Should().Be("clock");
    }

    [Fact]
    public void ConstructorShouldRejectNullLogger()
    {
        Action act = () => _ = new BackendProcessSupervisor(
            CreateConfig(),
            new FakeBackendProcessLauncher(),
            new FakeBackendRestartDelay(),
            new FakeClock(),
            null!);

        act.Should().Throw<ArgumentNullException>().Which.ParamName.Should().Be("logger");
    }

    private static async Task WaitForHealth(
        BackendProcessSupervisor supervisor,
        Func<BackendHealth, bool> predicate,
        TimeSpan timeout)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            BackendHealth health = await supervisor.GetHealthAsync(CancellationToken.None);
            if (predicate(health))
            {
                return;
            }

            await Task.Delay(5);
        }

        BackendHealth finalHealth = await supervisor.GetHealthAsync(CancellationToken.None);
        predicate(finalHealth).Should().BeTrue(
            $"expected health predicate to be satisfied within {timeout}; final health was {finalHealth}");
    }

    private sealed class FakeBackendProcessLauncher : IBackendProcessLauncher
    {
        private readonly Queue<Func<IBackendProcessHandle>> factories = new();
        private readonly Channel<int> launchEvents = Channel.CreateUnbounded<int>();
        private readonly List<BackendConfiguration> launchedConfigurations = [];
        private readonly Lock gate = new();

        public int LaunchCount
        {
            get { lock (gate) return launchedConfigurations.Count; }
        }

        public IReadOnlyList<BackendConfiguration> LaunchedConfigurations
        {
            get { lock (gate) return [.. launchedConfigurations]; }
        }

        public void EnqueueHandle(IBackendProcessHandle handle) =>
            factories.Enqueue(() => handle);

        public void EnqueueException(Exception exception) =>
            factories.Enqueue(() => throw exception);

        public IBackendProcessHandle Launch(BackendConfiguration configuration)
        {
            Func<IBackendProcessHandle> factory;
            lock (gate)
            {
                if (!factories.TryDequeue(out Func<IBackendProcessHandle>? next))
                {
                    throw new InvalidOperationException("No handle prepared for launch");
                }

                factory = next;
                launchedConfigurations.Add(configuration);
            }

            launchEvents.Writer.TryWrite(launchedConfigurations.Count);
            return factory();
        }

        public async Task WaitForLaunchCount(int expected, TimeSpan timeout)
        {
            using CancellationTokenSource cts = new(timeout);
            while (LaunchCount < expected)
            {
                await launchEvents.Reader.ReadAsync(cts.Token);
            }
        }
    }

    private sealed class FakeBackendRestartDelay : IBackendRestartDelay
    {
        private readonly List<TimeSpan> delays = [];
        private readonly Channel<TimeSpan> delayEvents = Channel.CreateUnbounded<TimeSpan>();
        private readonly Lock gate = new();

        public IReadOnlyList<TimeSpan> Delays
        {
            get { lock (gate) return [.. delays]; }
        }

        public Task WaitAsync(TimeSpan delay, CancellationToken cancellationToken)
        {
            lock (gate)
            {
                delays.Add(delay);
            }

            delayEvents.Writer.TryWrite(delay);
            return Task.CompletedTask;
        }

        public async Task WaitForDelayCount(int expected, TimeSpan timeout)
        {
            using CancellationTokenSource cts = new(timeout);
            while (Delays.Count < expected)
            {
                await delayEvents.Reader.ReadAsync(cts.Token);
            }
        }
    }

    private sealed class BlockingFakeBackendRestartDelay : IBackendRestartDelay
    {
        private readonly TaskCompletionSource firstInvocation =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource release =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task WaitAsync(TimeSpan delay, CancellationToken cancellationToken)
        {
            firstInvocation.TrySetResult();
            await using CancellationTokenRegistration registration = cancellationToken.Register(() =>
                release.TrySetCanceled(cancellationToken));
            await release.Task;
        }

        public void Release() => release.TrySetResult();

        public Task WaitForFirstInvocation(TimeSpan timeout) =>
            firstInvocation.Task.WaitAsync(timeout);
    }

    private sealed class FakeClock : IClock
    {
        private DateTimeOffset current;

        public FakeClock() : this(DateTimeOffset.UnixEpoch)
        {
        }

        public FakeClock(DateTimeOffset initial)
        {
            current = initial;
        }

        public DateTimeOffset UtcNow => current;

        public void Advance(TimeSpan delta) => current += delta;
    }

    private sealed class FakeBackendProcessHandle : IBackendProcessHandle
    {
        private readonly TaskCompletionSource exitSource =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int? ExitCode { get; private set; }

        public int StopCalls { get; private set; }

        public TimeSpan LastGracefulTimeout { get; private set; }

        public bool Disposed { get; private set; }

        public async Task WaitForExitAsync(CancellationToken cancellationToken)
        {
            await using CancellationTokenRegistration registration = cancellationToken.Register(() =>
                exitSource.TrySetCanceled(cancellationToken));
            await exitSource.Task;
        }

        public ValueTask StopAsync(TimeSpan gracefulTimeout, CancellationToken cancellationToken)
        {
            StopCalls++;
            LastGracefulTimeout = gracefulTimeout;
            ExitCode ??= 0;
            exitSource.TrySetResult();
            return ValueTask.CompletedTask;
        }

        public void SignalExit(int exitCode)
        {
            ExitCode = exitCode;
            exitSource.TrySetResult();
        }

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class AutoExitFakeHandle : IBackendProcessHandle
    {
        public AutoExitFakeHandle(int exitCode)
        {
            ExitCode = exitCode;
        }

        public int? ExitCode { get; }

        public Task WaitForExitAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public ValueTask StopAsync(TimeSpan gracefulTimeout, CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
