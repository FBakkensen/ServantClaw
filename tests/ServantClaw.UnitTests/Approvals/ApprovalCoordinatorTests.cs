using System.Diagnostics.CodeAnalysis;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using ServantClaw.Application.Approvals;
using ServantClaw.Application.Commands;
using ServantClaw.Domain.Agents;
using ServantClaw.Domain.Approvals;
using ServantClaw.Domain.Common;
using ServantClaw.Domain.Configuration;
using ServantClaw.Domain.Routing;
using ServantClaw.Domain.Runtime;
using ServantClaw.Domain.State;
using ServantClaw.UnitTests.Testing;
using Xunit;

namespace ServantClaw.UnitTests.Approvals;

[SuppressMessage(
    "Reliability",
    "CA2012:Use ValueTasks correctly",
    Justification = "NSubstitute's Returns() accepts pre-constructed ValueTask instances as test doubles; they are not orphaned async tasks.")]
public sealed class ApprovalCoordinatorTests
{
    private static readonly ChatId SampleChatId = new(100);
    private static readonly ApprovalContext SampleContext = new(
        SampleChatId,
        AgentKind.Coding,
        new ProjectId("repo"),
        new ThreadReference("thread-1"));
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task WaitForDecisionAsyncShouldPersistRecordBeforeSendingNotification()
    {
        InMemoryStateStore stateStore = new();
        OrderTrackingReplySink sink = new(stateStore);
        FixedClock clock = new(DateTimeOffset.UtcNow);
        ApprovalCoordinator coordinator = CreateCoordinator(stateStore, sink, clock);

        ApprovalRecord record = CreateRecord("A1", clock.UtcNow);

        Task<ApprovalDecision> waiter = coordinator
            .WaitForDecisionAsync(record, CancellationToken.None)
            .AsTask();

        await sink.FirstMessageSent.WaitAsync(TestTimeout);

        sink.StoredRecordWhenSendStarted.Should().NotBeNull();
        sink.StoredRecordWhenSendStarted!.IsPending.Should().BeTrue();

        ApprovalResolutionResult result = await coordinator.ResolveAsync(
            record.ApprovalId,
            SampleChatId,
            ApprovalDecision.Approved,
            CancellationToken.None);

        result.Outcome.Should().Be(ApprovalResolutionOutcome.Resolved);
        (await waiter.WaitAsync(TestTimeout)).Should().Be(ApprovalDecision.Approved);
    }

    [Fact]
    public async Task WaitForDecisionAsyncShouldSendNotificationToApprovalChat()
    {
        InMemoryStateStore stateStore = new();
        RecordingChatReplySink sink = new();
        FixedClock clock = new(DateTimeOffset.UtcNow);
        ApprovalCoordinator coordinator = CreateCoordinator(stateStore, sink, clock);
        ApprovalRecord record = CreateRecord("A1", clock.UtcNow);

        Task<ApprovalDecision> waiter = coordinator
            .WaitForDecisionAsync(record, CancellationToken.None)
            .AsTask();

        await sink.FirstMessageSent.WaitAsync(TestTimeout);

        sink.Messages.Should().ContainSingle();
        sink.Messages[0].ChatId.Should().Be(SampleChatId);
        sink.Messages[0].Text.Should().Contain("A1").And.Contain("/approve A1").And.Contain("/deny A1").And.Contain("Run: ls");

        await coordinator.ResolveAsync(record.ApprovalId, SampleChatId, ApprovalDecision.Approved, CancellationToken.None);
        await waiter.WaitAsync(TestTimeout);
    }

    [Fact]
    public async Task WaitForDecisionAsyncShouldReturnApprovedWhenOwnerApproves()
    {
        InMemoryStateStore stateStore = new();
        RecordingChatReplySink sink = new();
        FixedClock clock = new(DateTimeOffset.UtcNow);
        ApprovalCoordinator coordinator = CreateCoordinator(stateStore, sink, clock);
        ApprovalRecord record = CreateRecord("A1", clock.UtcNow);

        Task<ApprovalDecision> waiter = coordinator
            .WaitForDecisionAsync(record, CancellationToken.None)
            .AsTask();

        await sink.FirstMessageSent.WaitAsync(TestTimeout);
        clock.Advance(TimeSpan.FromSeconds(3));

        ApprovalResolutionResult result = await coordinator.ResolveAsync(
            record.ApprovalId,
            SampleChatId,
            ApprovalDecision.Approved,
            CancellationToken.None);

        result.Outcome.Should().Be(ApprovalResolutionOutcome.Resolved);
        result.Message.Should().Contain("A1").And.Contain("accepted");
        (await waiter.WaitAsync(TestTimeout)).Should().Be(ApprovalDecision.Approved);

        ApprovalRecord stored = stateStore.Approvals[record.ApprovalId];
        stored.Decision.Should().Be(ApprovalDecision.Approved);
        stored.ResolvedAt.Should().Be(clock.UtcNow);
        stored.IsPending.Should().BeFalse();
    }

    [Fact]
    public async Task WaitForDecisionAsyncShouldReturnDeniedWhenOwnerDenies()
    {
        InMemoryStateStore stateStore = new();
        RecordingChatReplySink sink = new();
        FixedClock clock = new(DateTimeOffset.UtcNow);
        ApprovalCoordinator coordinator = CreateCoordinator(stateStore, sink, clock);
        ApprovalRecord record = CreateRecord("A2", clock.UtcNow);

        Task<ApprovalDecision> waiter = coordinator
            .WaitForDecisionAsync(record, CancellationToken.None)
            .AsTask();

        await sink.FirstMessageSent.WaitAsync(TestTimeout);

        ApprovalResolutionResult result = await coordinator.ResolveAsync(
            record.ApprovalId,
            SampleChatId,
            ApprovalDecision.Denied,
            CancellationToken.None);

        result.Outcome.Should().Be(ApprovalResolutionOutcome.Resolved);
        result.Message.Should().Contain("A2").And.Contain("denied");
        (await waiter.WaitAsync(TestTimeout)).Should().Be(ApprovalDecision.Denied);

        stateStore.Approvals[record.ApprovalId].Decision.Should().Be(ApprovalDecision.Denied);
    }

    [Fact]
    public async Task ResolveAsyncShouldReturnUnknownWhenApprovalIdIsNotPersisted()
    {
        InMemoryStateStore stateStore = new();
        ApprovalCoordinator coordinator = CreateCoordinator(stateStore);

        ApprovalResolutionResult result = await coordinator.ResolveAsync(
            new ApprovalId("missing"),
            SampleChatId,
            ApprovalDecision.Approved,
            CancellationToken.None);

        result.Outcome.Should().Be(ApprovalResolutionOutcome.UnknownId);
        result.Message.Should().Be("Approval 'missing' was not found.");
        stateStore.Approvals.Should().BeEmpty();
    }

    [Fact]
    public async Task ResolveAsyncShouldReturnAlreadyResolvedWhenRecordIsApproved()
    {
        InMemoryStateStore stateStore = new();
        FixedClock clock = new(DateTimeOffset.UtcNow);
        ApprovalRecord record = CreateRecord("A1", clock.UtcNow);
        stateStore.Approvals[record.ApprovalId] = record.Resolve(ApprovalDecision.Approved, clock.UtcNow);
        ApprovalCoordinator coordinator = CreateCoordinator(stateStore, clock: clock);

        ApprovalResolutionResult result = await coordinator.ResolveAsync(
            record.ApprovalId,
            SampleChatId,
            ApprovalDecision.Approved,
            CancellationToken.None);

        result.Outcome.Should().Be(ApprovalResolutionOutcome.AlreadyResolved);
        result.Message.Should().Be("Approval 'A1' was already approved.");
        stateStore.Approvals[record.ApprovalId].Decision.Should().Be(ApprovalDecision.Approved);
    }

    [Fact]
    public async Task ResolveAsyncShouldReturnAlreadyResolvedWhenRecordIsDenied()
    {
        InMemoryStateStore stateStore = new();
        FixedClock clock = new(DateTimeOffset.UtcNow);
        ApprovalRecord record = CreateRecord("A1", clock.UtcNow);
        stateStore.Approvals[record.ApprovalId] = record.Resolve(ApprovalDecision.Denied, clock.UtcNow);
        ApprovalCoordinator coordinator = CreateCoordinator(stateStore, clock: clock);

        ApprovalResolutionResult result = await coordinator.ResolveAsync(
            record.ApprovalId,
            SampleChatId,
            ApprovalDecision.Approved,
            CancellationToken.None);

        result.Outcome.Should().Be(ApprovalResolutionOutcome.AlreadyResolved);
        result.Message.Should().Be("Approval 'A1' was already denied.");
    }

    [Fact]
    public async Task ResolveAsyncShouldReturnWrongChatWhenCommandChatDoesNotOwnRecord()
    {
        InMemoryStateStore stateStore = new();
        RecordingChatReplySink sink = new();
        FixedClock clock = new(DateTimeOffset.UtcNow);
        ApprovalCoordinator coordinator = CreateCoordinator(stateStore, sink, clock);
        ApprovalRecord record = CreateRecord("A1", clock.UtcNow);

        Task<ApprovalDecision> waiter = coordinator
            .WaitForDecisionAsync(record, CancellationToken.None)
            .AsTask();

        await sink.FirstMessageSent.WaitAsync(TestTimeout);

        ApprovalResolutionResult result = await coordinator.ResolveAsync(
            record.ApprovalId,
            new ChatId(999),
            ApprovalDecision.Approved,
            CancellationToken.None);

        result.Outcome.Should().Be(ApprovalResolutionOutcome.WrongChat);
        result.Message.Should().Be("Approval 'A1' does not belong to this chat.");
        stateStore.Approvals[record.ApprovalId].IsPending.Should().BeTrue();
        waiter.IsCompleted.Should().BeFalse();

        await coordinator.ResolveAsync(record.ApprovalId, SampleChatId, ApprovalDecision.Denied, CancellationToken.None);
        await waiter.WaitAsync(TestTimeout);
    }

    [Fact]
    public async Task ResolveAsyncShouldReturnNotActiveWhenNoTaskCompletionSourceExists()
    {
        InMemoryStateStore stateStore = new();
        FixedClock clock = new(DateTimeOffset.UtcNow);
        ApprovalRecord record = CreateRecord("A1", clock.UtcNow);
        stateStore.Approvals[record.ApprovalId] = record;
        ApprovalCoordinator coordinator = CreateCoordinator(stateStore, clock: clock);

        ApprovalResolutionResult result = await coordinator.ResolveAsync(
            record.ApprovalId,
            SampleChatId,
            ApprovalDecision.Approved,
            CancellationToken.None);

        result.Outcome.Should().Be(ApprovalResolutionOutcome.NotActive);
        result.Message.Should().Be("Approval 'A1' is no longer active.");
        stateStore.Approvals[record.ApprovalId].IsPending.Should().BeTrue();
    }

    [Fact]
    public async Task PersistedResolvedRecordShouldRetainFullAuditMetadata()
    {
        InMemoryStateStore stateStore = new();
        RecordingChatReplySink sink = new();
        DateTimeOffset createdAt = new(2026, 4, 21, 12, 0, 0, TimeSpan.Zero);
        FixedClock clock = new(createdAt);
        ApprovalCoordinator coordinator = CreateCoordinator(stateStore, sink, clock);
        IReadOnlyDictionary<string, string> metadata = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["method"] = "item/commandExecution/requestApproval",
            ["command"] = "git status",
            ["cwd"] = "/repo"
        };
        ApprovalRecord record = new(
            new ApprovalId("A1"),
            ApprovalClass.StandardRiskyAction,
            SampleContext,
            "Run: git status",
            createdAt,
            metadata);

        Task<ApprovalDecision> waiter = coordinator
            .WaitForDecisionAsync(record, CancellationToken.None)
            .AsTask();

        await sink.FirstMessageSent.WaitAsync(TestTimeout);
        clock.Advance(TimeSpan.FromSeconds(10));

        await coordinator.ResolveAsync(record.ApprovalId, SampleChatId, ApprovalDecision.Approved, CancellationToken.None);
        await waiter.WaitAsync(TestTimeout);

        ApprovalRecord stored = stateStore.Approvals[record.ApprovalId];
        stored.ApprovalClass.Should().Be(ApprovalClass.StandardRiskyAction);
        stored.Context.ChatId.Should().Be(SampleChatId);
        stored.Context.Agent.Should().Be(AgentKind.Coding);
        stored.Context.ProjectId.Should().Be(new ProjectId("repo"));
        stored.Context.ThreadReference.Should().Be(new ThreadReference("thread-1"));
        stored.Summary.Should().Be("Run: git status");
        stored.CreatedAt.Should().Be(createdAt);
        stored.ResolvedAt.Should().Be(createdAt + TimeSpan.FromSeconds(10));
        stored.Decision.Should().Be(ApprovalDecision.Approved);
        stored.OperationMetadata.Should().Contain(new KeyValuePair<string, string>("command", "git status"));
        stored.OperationMetadata.Should().Contain(new KeyValuePair<string, string>("cwd", "/repo"));
    }

    [Fact]
    public async Task ResolveAsyncShouldRejectSecondResolutionAfterFirstSucceeds()
    {
        InMemoryStateStore stateStore = new();
        RecordingChatReplySink sink = new();
        FixedClock clock = new(DateTimeOffset.UtcNow);
        ApprovalCoordinator coordinator = CreateCoordinator(stateStore, sink, clock);
        ApprovalRecord record = CreateRecord("A1", clock.UtcNow);

        Task<ApprovalDecision> waiter = coordinator
            .WaitForDecisionAsync(record, CancellationToken.None)
            .AsTask();

        await sink.FirstMessageSent.WaitAsync(TestTimeout);

        await coordinator.ResolveAsync(record.ApprovalId, SampleChatId, ApprovalDecision.Approved, CancellationToken.None);
        await waiter.WaitAsync(TestTimeout);

        ApprovalResolutionResult second = await coordinator.ResolveAsync(
            record.ApprovalId,
            SampleChatId,
            ApprovalDecision.Denied,
            CancellationToken.None);

        second.Outcome.Should().Be(ApprovalResolutionOutcome.AlreadyResolved);
        stateStore.Approvals[record.ApprovalId].Decision.Should().Be(ApprovalDecision.Approved);
    }

    [Fact]
    public async Task WaitForDecisionAsyncShouldPropagateCancellationWithoutResolvingRecord()
    {
        InMemoryStateStore stateStore = new();
        RecordingChatReplySink sink = new();
        FixedClock clock = new(DateTimeOffset.UtcNow);
        ApprovalCoordinator coordinator = CreateCoordinator(stateStore, sink, clock);
        ApprovalRecord record = CreateRecord("A1", clock.UtcNow);

        using CancellationTokenSource cts = new();

        Task<ApprovalDecision> waiter = coordinator
            .WaitForDecisionAsync(record, cts.Token)
            .AsTask();

        await sink.FirstMessageSent.WaitAsync(TestTimeout);
        cts.Cancel();

        Func<Task> act = async () => await waiter.WaitAsync(TestTimeout);
        await act.Should().ThrowAsync<OperationCanceledException>();

        stateStore.Approvals[record.ApprovalId].IsPending.Should().BeTrue();

        ApprovalResolutionResult result = await coordinator.ResolveAsync(
            record.ApprovalId,
            SampleChatId,
            ApprovalDecision.Approved,
            CancellationToken.None);
        result.Outcome.Should().Be(ApprovalResolutionOutcome.NotActive);
    }

    [Fact]
    public async Task ConcurrentWaitForDecisionOnSameIdShouldRejectSecond()
    {
        InMemoryStateStore stateStore = new();
        RecordingChatReplySink sink = new();
        FixedClock clock = new(DateTimeOffset.UtcNow);
        ApprovalCoordinator coordinator = CreateCoordinator(stateStore, sink, clock);
        ApprovalRecord record = CreateRecord("A1", clock.UtcNow);

        Task<ApprovalDecision> first = coordinator
            .WaitForDecisionAsync(record, CancellationToken.None)
            .AsTask();

        await sink.FirstMessageSent.WaitAsync(TestTimeout);

        Func<Task> act = async () => await coordinator.WaitForDecisionAsync(record, CancellationToken.None);
        await act.Should().ThrowAsync<InvalidOperationException>();

        await coordinator.ResolveAsync(record.ApprovalId, SampleChatId, ApprovalDecision.Denied, CancellationToken.None);
        await first.WaitAsync(TestTimeout);
    }

    [Fact]
    public async Task WaitForDecisionAsyncShouldThrowWhenRecordIsNull()
    {
        ApprovalCoordinator coordinator = CreateCoordinator(new InMemoryStateStore());

        Func<Task> act = async () => await coordinator.WaitForDecisionAsync(null!, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task ResolveAsyncShouldFailWaiterAndRethrowWhenSaveFails()
    {
        IStateStore stateStore = Substitute.For<IStateStore>();
        RecordingChatReplySink sink = new();
        FixedClock clock = new(DateTimeOffset.UtcNow);
        ApprovalRecord record = CreateRecord("A1", clock.UtcNow);
        InvalidOperationException failure = new("simulated save failure");

        int saveCalls = 0;
        stateStore.SaveApprovalAsync(Arg.Any<ApprovalRecord>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                saveCalls++;
                return saveCalls == 1
                    ? ValueTask.CompletedTask
                    : throw failure;
            });
        stateStore.GetApprovalAsync(record.ApprovalId, Arg.Any<CancellationToken>())
            .Returns(new ValueTask<ApprovalRecord?>(record));

        ApprovalCoordinator coordinator = new(stateStore, sink, clock, NullLogger<ApprovalCoordinator>.Instance);

        Task<ApprovalDecision> waiter = coordinator
            .WaitForDecisionAsync(record, CancellationToken.None)
            .AsTask();

        await sink.FirstMessageSent.WaitAsync(TestTimeout);

        Func<Task> resolveAct = async () => await coordinator.ResolveAsync(
            record.ApprovalId,
            SampleChatId,
            ApprovalDecision.Approved,
            CancellationToken.None);

        (await resolveAct.Should().ThrowAsync<InvalidOperationException>())
            .Which.Should().BeSameAs(failure);

        Func<Task> waiterAct = async () => await waiter.WaitAsync(TestTimeout);
        (await waiterAct.Should().ThrowAsync<InvalidOperationException>())
            .Which.Should().BeSameAs(failure);
    }

    [Fact]
    public void ApprovalResolutionResultShouldRejectBlankMessage()
    {
        Action nullMessage = () => _ = new ApprovalResolutionResult(ApprovalResolutionOutcome.Resolved, null!);
        Action whitespaceMessage = () => _ = new ApprovalResolutionResult(ApprovalResolutionOutcome.Resolved, "   ");

        nullMessage.Should().Throw<ArgumentException>().Which.ParamName.Should().Be("Message");
        whitespaceMessage.Should().Throw<ArgumentException>().Which.ParamName.Should().Be("Message");
    }

    [Fact]
    public void ApprovalResolutionResultShouldTrimMessage()
    {
        ApprovalResolutionResult result = new(ApprovalResolutionOutcome.Resolved, "  hello  ");

        result.Message.Should().Be("hello");
    }

    [Fact]
    public void ConstructorShouldRejectNullDependencies()
    {
        InMemoryStateStore stateStore = new();
        RecordingChatReplySink sink = new();
        FixedClock clock = new(DateTimeOffset.UtcNow);

        Action nullStateStore = () => _ = new ApprovalCoordinator(null!, sink, clock, NullLogger<ApprovalCoordinator>.Instance);
        Action nullSink = () => _ = new ApprovalCoordinator(stateStore, null!, clock, NullLogger<ApprovalCoordinator>.Instance);
        Action nullClock = () => _ = new ApprovalCoordinator(stateStore, sink, null!, NullLogger<ApprovalCoordinator>.Instance);
        Action nullLogger = () => _ = new ApprovalCoordinator(stateStore, sink, clock, null!);

        nullStateStore.Should().Throw<ArgumentNullException>().Which.ParamName.Should().Be("stateStore");
        nullSink.Should().Throw<ArgumentNullException>().Which.ParamName.Should().Be("chatReplySink");
        nullClock.Should().Throw<ArgumentNullException>().Which.ParamName.Should().Be("clock");
        nullLogger.Should().Throw<ArgumentNullException>().Which.ParamName.Should().Be("logger");
    }

    private static ApprovalCoordinator CreateCoordinator(
        InMemoryStateStore stateStore,
        IChatReplySink? sink = null,
        FixedClock? clock = null) =>
        new(
            stateStore,
            sink ?? new RecordingChatReplySink(),
            clock ?? new FixedClock(DateTimeOffset.UtcNow),
            NullLogger<ApprovalCoordinator>.Instance);

    private static ApprovalRecord CreateRecord(string id, DateTimeOffset createdAt) =>
        new(
            new ApprovalId(id),
            ApprovalClass.StandardRiskyAction,
            SampleContext,
            "Run: ls",
            createdAt);

    private sealed class RecordingChatReplySink : IChatReplySink
    {
        private readonly TaskCompletionSource firstMessage = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public List<(ChatId ChatId, string Text)> Messages { get; } = [];

        public Task FirstMessageSent => firstMessage.Task;

        public ValueTask SendMessageAsync(ChatId chatId, string message, CancellationToken cancellationToken)
        {
            Messages.Add((chatId, message));
            firstMessage.TrySetResult();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class OrderTrackingReplySink : IChatReplySink
    {
        private readonly InMemoryStateStore stateStore;
        private readonly TaskCompletionSource firstMessage = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public OrderTrackingReplySink(InMemoryStateStore stateStore)
        {
            this.stateStore = stateStore;
        }

        public ApprovalRecord? StoredRecordWhenSendStarted { get; private set; }

        public Task FirstMessageSent => firstMessage.Task;

        public ValueTask SendMessageAsync(ChatId chatId, string message, CancellationToken cancellationToken)
        {
            StoredRecordWhenSendStarted ??= stateStore.Approvals.Values.FirstOrDefault();
            firstMessage.TrySetResult();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FixedClock : IClock
    {
        private DateTimeOffset current;

        public FixedClock(DateTimeOffset initial)
        {
            current = initial;
        }

        public DateTimeOffset UtcNow => current;

        public void Advance(TimeSpan amount)
        {
            current = current.Add(amount);
        }
    }
}
