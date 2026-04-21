using System.Threading.Channels;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using ServantClaw.Application.Runtime;
using ServantClaw.Domain.Agents;
using ServantClaw.Domain.Common;
using ServantClaw.Domain.Routing;
using Xunit;

namespace ServantClaw.UnitTests.Runtime;

public sealed class PerContextTurnQueueTests
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task EnqueueAsyncShouldDispatchTurnToExecutor()
    {
        RecordingTurnExecutor executor = new();
        PerContextTurnQueue queue = CreateQueue(executor);

        await queue.StartAsync(CancellationToken.None);

        QueuedTurn turn = CreateTurn("hello");
        await queue.EnqueueAsync(turn, CancellationToken.None);

        QueuedTurn received = await executor.WaitForExecution(TestTimeout);

        received.Should().Be(turn);

        await queue.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task TurnsInTheSameContextShouldExecuteInArrivalOrder()
    {
        GatedTurnExecutor executor = new();
        PerContextTurnQueue queue = CreateQueue(executor);

        await queue.StartAsync(CancellationToken.None);

        QueuedTurn first = CreateTurn("first");
        QueuedTurn second = CreateTurn("second");

        await queue.EnqueueAsync(first, CancellationToken.None);
        await queue.EnqueueAsync(second, CancellationToken.None);

        QueuedTurn firstInvocation = await executor.WaitForInvocation(TestTimeout);
        firstInvocation.Should().Be(first);

        executor.InFlight.Should().ContainSingle();
        executor.Completed.Should().BeEmpty();

        executor.Release(first);
        QueuedTurn secondInvocation = await executor.WaitForInvocation(TestTimeout);
        secondInvocation.Should().Be(second);

        executor.Release(second);
        await executor.WaitForAllCompleted(2, TestTimeout);

        await queue.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task DifferentContextsShouldExecuteConcurrently()
    {
        GatedTurnExecutor executor = new();
        PerContextTurnQueue queue = CreateQueue(executor);

        await queue.StartAsync(CancellationToken.None);

        QueuedTurn onContextX = CreateTurn("x", chatId: 1);
        QueuedTurn onContextY = CreateTurn("y", chatId: 2);

        await queue.EnqueueAsync(onContextX, CancellationToken.None);
        await queue.EnqueueAsync(onContextY, CancellationToken.None);

        await executor.WaitForInFlightCount(2, TestTimeout);

        executor.Release(onContextX);
        executor.Release(onContextY);
        await executor.WaitForAllCompleted(2, TestTimeout);

        await queue.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task SlowTurnInOneContextShouldNotBlockAnotherContext()
    {
        GatedTurnExecutor executor = new();
        PerContextTurnQueue queue = CreateQueue(executor);

        await queue.StartAsync(CancellationToken.None);

        QueuedTurn slow = CreateTurn("slow", chatId: 1);
        QueuedTurn fast = CreateTurn("fast", chatId: 2);

        await queue.EnqueueAsync(slow, CancellationToken.None);
        QueuedTurn slowInvocation = await executor.WaitForInvocation(TestTimeout);
        slowInvocation.Should().Be(slow);

        await queue.EnqueueAsync(fast, CancellationToken.None);
        QueuedTurn fastInvocation = await executor.WaitForInvocation(TestTimeout);
        fastInvocation.Should().Be(fast);

        executor.Release(fast);
        QueuedTurn fastCompletion = await executor.WaitForCompletion(TestTimeout);
        fastCompletion.Should().Be(fast);

        executor.Release(slow);
        QueuedTurn slowCompletion = await executor.WaitForCompletion(TestTimeout);
        slowCompletion.Should().Be(slow);

        await queue.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task ExecutorExceptionShouldNotBlockSubsequentTurnsInSameContext()
    {
        ThrowingOnceTurnExecutor executor = new();
        PerContextTurnQueue queue = CreateQueue(executor);

        await queue.StartAsync(CancellationToken.None);

        QueuedTurn failing = CreateTurn("failing");
        QueuedTurn followUp = CreateTurn("follow-up");

        await queue.EnqueueAsync(failing, CancellationToken.None);
        await queue.EnqueueAsync(followUp, CancellationToken.None);

        QueuedTurn successfulTurn = await executor.WaitForSuccessfulTurn(TestTimeout);

        successfulTurn.Should().Be(followUp);
        executor.ThrowCount.Should().Be(1);

        await queue.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task StopAsyncShouldCancelInFlightTurnAndReturnPromptly()
    {
        CancellationObservingExecutor executor = new();
        PerContextTurnQueue queue = CreateQueue(executor);

        await queue.StartAsync(CancellationToken.None);

        await queue.EnqueueAsync(CreateTurn("long-running"), CancellationToken.None);
        await executor.WaitForInvocation(TestTimeout);

        await queue.StopAsync(CancellationToken.None);

        executor.ObservedCancellation.Should().BeTrue();
    }

    [Fact]
    public async Task EnqueueAsyncShouldRejectNullTurn()
    {
        PerContextTurnQueue queue = CreateQueue(new RecordingTurnExecutor());
        await queue.StartAsync(CancellationToken.None);

        Func<Task> act = async () => await queue.EnqueueAsync(null!, CancellationToken.None);

        (await act.Should().ThrowAsync<ArgumentNullException>()).Which.ParamName.Should().Be("turn");

        await queue.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task EnqueueAsyncBeforeStartAsyncShouldThrow()
    {
        PerContextTurnQueue queue = CreateQueue(new RecordingTurnExecutor());

        Func<Task> act = async () => await queue.EnqueueAsync(CreateTurn("nope"), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task EnqueueAsyncAfterStopAsyncShouldThrow()
    {
        PerContextTurnQueue queue = CreateQueue(new RecordingTurnExecutor());
        await queue.StartAsync(CancellationToken.None);
        await queue.StopAsync(CancellationToken.None);

        Func<Task> act = async () => await queue.EnqueueAsync(CreateTurn("nope"), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task StartAsyncCalledTwiceShouldThrow()
    {
        PerContextTurnQueue queue = CreateQueue(new RecordingTurnExecutor());

        await queue.StartAsync(CancellationToken.None);

        Func<Task> act = async () => await queue.StartAsync(CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();

        await queue.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task StopAsyncWithoutStartShouldBeNoOp()
    {
        PerContextTurnQueue queue = CreateQueue(new RecordingTurnExecutor());

        Func<Task> act = async () => await queue.StopAsync(CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task StopAsyncCalledTwiceShouldBeNoOp()
    {
        PerContextTurnQueue queue = CreateQueue(new RecordingTurnExecutor());

        await queue.StartAsync(CancellationToken.None);
        await queue.StopAsync(CancellationToken.None);

        Func<Task> act = async () => await queue.StopAsync(CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task StartAsyncAfterStopAsyncShouldSucceed()
    {
        RecordingTurnExecutor executor = new();
        PerContextTurnQueue queue = CreateQueue(executor);

        await queue.StartAsync(CancellationToken.None);
        await queue.StopAsync(CancellationToken.None);
        await queue.StartAsync(CancellationToken.None);

        QueuedTurn turn = CreateTurn("second-lifecycle");
        await queue.EnqueueAsync(turn, CancellationToken.None);
        QueuedTurn received = await executor.WaitForExecution(TestTimeout);

        received.Should().Be(turn);

        await queue.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task RestartingLifecycleShouldNotReuseFirstLifecycleWorkersForSameContext()
    {
        MultiRecordingTurnExecutor executor = new();
        PerContextTurnQueue queue = CreateQueue(executor);

        QueuedTurn firstLifecycle = CreateTurn("first-lifecycle", chatId: 500);
        QueuedTurn secondLifecycle = CreateTurn("second-lifecycle", chatId: 500);

        await queue.StartAsync(CancellationToken.None);
        await queue.EnqueueAsync(firstLifecycle, CancellationToken.None);
        (await executor.WaitForNextExecution(TestTimeout)).Should().Be(firstLifecycle);
        await queue.StopAsync(CancellationToken.None);

        await queue.StartAsync(CancellationToken.None);
        await queue.EnqueueAsync(secondLifecycle, CancellationToken.None);
        (await executor.WaitForNextExecution(TestTimeout)).Should().Be(secondLifecycle);

        await queue.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task ExecutorExceptionShouldBeLoggedAtErrorLevel()
    {
        ILogger<PerContextTurnQueue> logger = Substitute.For<ILogger<PerContextTurnQueue>>();
        logger.IsEnabled(Arg.Any<LogLevel>()).Returns(true);
        ThrowingOnceTurnExecutor executor = new();
        PerContextTurnQueue queue = new(executor, logger);

        await queue.StartAsync(CancellationToken.None);
        await queue.EnqueueAsync(CreateTurn("failing"), CancellationToken.None);
        await queue.EnqueueAsync(CreateTurn("follow-up"), CancellationToken.None);
        await executor.WaitForSuccessfulTurn(TestTimeout);

        logger.Received(1).Log(
            LogLevel.Error,
            Arg.Any<EventId>(),
            Arg.Any<object>(),
            Arg.Any<Exception?>(),
            Arg.Any<Func<object, Exception?, string>>());

        await queue.StopAsync(CancellationToken.None);
    }

    [Fact]
    public void ConstructorShouldRejectNullExecutor()
    {
        Action act = () => _ = new PerContextTurnQueue(null!, NullLogger<PerContextTurnQueue>.Instance);

        act.Should().Throw<ArgumentNullException>().Which.ParamName.Should().Be("executor");
    }

    [Fact]
    public void ConstructorShouldRejectNullLogger()
    {
        Action act = () => _ = new PerContextTurnQueue(new RecordingTurnExecutor(), null!);

        act.Should().Throw<ArgumentNullException>().Which.ParamName.Should().Be("logger");
    }

    private static PerContextTurnQueue CreateQueue(ITurnExecutor executor) =>
        new(executor, NullLogger<PerContextTurnQueue>.Instance);

    private static QueuedTurn CreateTurn(string messageText, long chatId = 100) =>
        new(
            new ThreadContext(new ChatId(chatId), AgentKind.Coding, new ProjectId("project")),
            messageText,
            DateTimeOffset.UtcNow);

    private sealed class RecordingTurnExecutor : ITurnExecutor
    {
        private readonly TaskCompletionSource<QueuedTurn> nextExecution =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ValueTask ExecuteAsync(QueuedTurn turn, CancellationToken cancellationToken)
        {
            nextExecution.TrySetResult(turn);
            return ValueTask.CompletedTask;
        }

        public Task<QueuedTurn> WaitForExecution(TimeSpan timeout) =>
            nextExecution.Task.WaitAsync(timeout);
    }

    private sealed class MultiRecordingTurnExecutor : ITurnExecutor
    {
        private readonly Channel<QueuedTurn> executions = Channel.CreateUnbounded<QueuedTurn>();

        public async ValueTask ExecuteAsync(QueuedTurn turn, CancellationToken cancellationToken)
        {
            await executions.Writer.WriteAsync(turn, cancellationToken);
        }

        public async Task<QueuedTurn> WaitForNextExecution(TimeSpan timeout)
        {
            using CancellationTokenSource cts = new(timeout);
            return await executions.Reader.ReadAsync(cts.Token);
        }
    }

    private sealed class GatedTurnExecutor : ITurnExecutor
    {
        private readonly object gate = new();
        private readonly Dictionary<QueuedTurn, TaskCompletionSource> gates = [];
        private readonly List<QueuedTurn> inFlight = [];
        private readonly List<QueuedTurn> completed = [];
        private readonly Channel<QueuedTurn> invocationEvents = Channel.CreateUnbounded<QueuedTurn>();
        private readonly Channel<QueuedTurn> completionEvents = Channel.CreateUnbounded<QueuedTurn>();

        public IReadOnlyList<QueuedTurn> InFlight
        {
            get { lock (gate) return [.. inFlight]; }
        }

        public IReadOnlyList<QueuedTurn> Completed
        {
            get { lock (gate) return [.. completed]; }
        }

        public async ValueTask ExecuteAsync(QueuedTurn turn, CancellationToken cancellationToken)
        {
            TaskCompletionSource turnGate = new(TaskCreationOptions.RunContinuationsAsynchronously);

            lock (gate)
            {
                gates[turn] = turnGate;
                inFlight.Add(turn);
            }

            await invocationEvents.Writer.WriteAsync(turn, cancellationToken);

            try
            {
                await turnGate.Task.WaitAsync(cancellationToken);
            }
            finally
            {
                lock (gate)
                {
                    inFlight.Remove(turn);
                    completed.Add(turn);
                }

                await completionEvents.Writer.WriteAsync(turn, CancellationToken.None);
            }
        }

        public void Release(QueuedTurn turn)
        {
            TaskCompletionSource? turnGate;
            lock (gate)
            {
                gates.TryGetValue(turn, out turnGate);
            }

            turnGate?.TrySetResult();
        }

        public async Task<QueuedTurn> WaitForInvocation(TimeSpan timeout)
        {
            using CancellationTokenSource cts = new(timeout);
            return await invocationEvents.Reader.ReadAsync(cts.Token);
        }

        public async Task<QueuedTurn> WaitForCompletion(TimeSpan timeout)
        {
            using CancellationTokenSource cts = new(timeout);
            return await completionEvents.Reader.ReadAsync(cts.Token);
        }

        public async Task WaitForInFlightCount(int expected, TimeSpan timeout)
        {
            DateTimeOffset deadline = DateTimeOffset.UtcNow + timeout;
            while (DateTimeOffset.UtcNow < deadline)
            {
                lock (gate)
                {
                    if (inFlight.Count >= expected)
                    {
                        return;
                    }
                }

                await Task.Delay(5);
            }

            lock (gate)
            {
                inFlight.Count.Should().BeGreaterThanOrEqualTo(expected);
            }
        }

        public async Task WaitForAllCompleted(int expected, TimeSpan timeout)
        {
            DateTimeOffset deadline = DateTimeOffset.UtcNow + timeout;
            while (DateTimeOffset.UtcNow < deadline)
            {
                lock (gate)
                {
                    if (completed.Count >= expected)
                    {
                        return;
                    }
                }

                await Task.Delay(5);
            }

            lock (gate)
            {
                completed.Count.Should().BeGreaterThanOrEqualTo(expected);
            }
        }
    }

    private sealed class ThrowingOnceTurnExecutor : ITurnExecutor
    {
        private readonly TaskCompletionSource<QueuedTurn> successfulTurn =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int ThrowCount { get; private set; }

        public ValueTask ExecuteAsync(QueuedTurn turn, CancellationToken cancellationToken)
        {
            if (ThrowCount == 0)
            {
                ThrowCount++;
                throw new InvalidOperationException("intentional failure");
            }

            successfulTurn.TrySetResult(turn);
            return ValueTask.CompletedTask;
        }

        public Task<QueuedTurn> WaitForSuccessfulTurn(TimeSpan timeout) =>
            successfulTurn.Task.WaitAsync(timeout);
    }

    private sealed class CancellationObservingExecutor : ITurnExecutor
    {
        private readonly TaskCompletionSource invocation =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool ObservedCancellation { get; private set; }

        public async ValueTask ExecuteAsync(QueuedTurn turn, CancellationToken cancellationToken)
        {
            invocation.TrySetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                ObservedCancellation = true;
                throw;
            }
        }

        public Task WaitForInvocation(TimeSpan timeout) => invocation.Task.WaitAsync(timeout);
    }
}
