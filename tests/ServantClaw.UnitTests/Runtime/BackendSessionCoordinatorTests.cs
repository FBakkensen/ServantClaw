using FluentAssertions;
using ServantClaw.Application.Runtime;
using Xunit;

namespace ServantClaw.UnitTests.Runtime;

public sealed class BackendSessionCoordinatorTests
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(5);

    [Fact]
    public void CurrentShouldBeNullBeforeAnyPublish()
    {
        BackendSessionCoordinator coordinator = new();

        coordinator.Current.Should().BeNull();
    }

    [Fact]
    public async Task PublishShouldExposeCurrentAndCompletePendingWaiter()
    {
        BackendSessionCoordinator coordinator = new();
        BackendSession session = CreateSession();

        ValueTask<BackendSession> waitTask = coordinator.WaitForSessionAsync(CancellationToken.None);
        coordinator.Publish(session);

        BackendSession resolved = await waitTask.AsTask().WaitAsync(TestTimeout);
        resolved.Should().BeSameAs(session);
        coordinator.Current.Should().BeSameAs(session);
    }

    [Fact]
    public async Task WaitForSessionAsyncShouldReturnImmediatelyWhenAlreadyPublished()
    {
        BackendSessionCoordinator coordinator = new();
        BackendSession session = CreateSession();
        coordinator.Publish(session);

        BackendSession resolved = await coordinator.WaitForSessionAsync(CancellationToken.None);

        resolved.Should().BeSameAs(session);
    }

    [Fact]
    public void RetractShouldClearCurrent()
    {
        BackendSessionCoordinator coordinator = new();
        coordinator.Publish(CreateSession());
        coordinator.Retract();

        coordinator.Current.Should().BeNull();
    }

    [Fact]
    public async Task RetractShouldRearmSoNextWaiterBlocksUntilNextPublish()
    {
        BackendSessionCoordinator coordinator = new();
        coordinator.Publish(CreateSession());
        coordinator.Retract();

        ValueTask<BackendSession> waitTask = coordinator.WaitForSessionAsync(CancellationToken.None);
        waitTask.IsCompleted.Should().BeFalse("after retract there is no live session yet");

        BackendSession next = CreateSession();
        coordinator.Publish(next);

        BackendSession resolved = await waitTask.AsTask().WaitAsync(TestTimeout);
        resolved.Should().BeSameAs(next);
    }

    [Fact]
    public async Task WaitForSessionAsyncShouldHonourCancellationBeforePublish()
    {
        BackendSessionCoordinator coordinator = new();
        using CancellationTokenSource cts = new();

        ValueTask<BackendSession> waitTask = coordinator.WaitForSessionAsync(cts.Token);
        await cts.CancelAsync();

        Func<Task> act = async () => await waitTask;
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task ConcurrentWaitersShouldAllReceiveThePublishedSession()
    {
        BackendSessionCoordinator coordinator = new();

        Task<BackendSession> first = coordinator.WaitForSessionAsync(CancellationToken.None).AsTask();
        Task<BackendSession> second = coordinator.WaitForSessionAsync(CancellationToken.None).AsTask();

        BackendSession session = CreateSession();
        coordinator.Publish(session);

        BackendSession[] resolved = await Task.WhenAll(first, second).WaitAsync(TestTimeout);
        resolved.Should().OnlyContain(s => ReferenceEquals(s, session));
    }

    [Fact]
    public void PublishShouldRejectNull()
    {
        BackendSessionCoordinator coordinator = new();

        Action act = () => coordinator.Publish(null!);

        act.Should().Throw<ArgumentNullException>().Which.ParamName.Should().Be("session");
    }

    [Fact]
    public void RetractCalledBeforeAnyPublishShouldBeNoOp()
    {
        BackendSessionCoordinator coordinator = new();

        Action act = coordinator.Retract;

        act.Should().NotThrow();
        coordinator.Current.Should().BeNull();
    }

    [Fact]
    public async Task RepeatedPublishShouldReplaceTheCurrentSession()
    {
        BackendSessionCoordinator coordinator = new();
        BackendSession first = CreateSession();
        BackendSession second = CreateSession();

        coordinator.Publish(first);
        coordinator.Publish(second);

        coordinator.Current.Should().BeSameAs(second);
        (await coordinator.WaitForSessionAsync(CancellationToken.None)).Should().BeSameAs(second);
    }

    private static BackendSession CreateSession(CancellationToken lifetime = default) =>
        new(new MemoryStream(), new MemoryStream(), new MemoryStream(), lifetime);
}
