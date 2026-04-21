using System.Diagnostics.CodeAnalysis;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using ServantClaw.Application.Commands;
using ServantClaw.Application.Runtime;
using ServantClaw.Domain.Agents;
using ServantClaw.Domain.Approvals;
using ServantClaw.Domain.Common;
using ServantClaw.Domain.Routing;
using ServantClaw.Domain.Runtime;
using ServantClaw.Domain.State;
using ServantClaw.UnitTests.Testing;
using Xunit;

namespace ServantClaw.UnitTests.Runtime;

[SuppressMessage(
    "Reliability",
    "CA2012:Use ValueTasks correctly",
    Justification = "NSubstitute's Returns() accepts pre-constructed ValueTask instances as test doubles; they are not orphaned async tasks.")]
public sealed class CodexTurnExecutorTests
{
    private static readonly ThreadContext SampleContext = new(
        new ChatId(42),
        AgentKind.Coding,
        new ProjectId("repo"));

    [Fact]
    public async Task FirstTurnShouldCreateCodexThreadAndPersistMapping()
    {
        InMemoryStateStore stateStore = new();
        RecordingChatReplySink sink = new();
        IBackendClient backend = Substitute.For<IBackendClient>();
        backend.CreateThreadAsync(SampleContext, Arg.Any<CancellationToken>())
            .Returns(new ValueTask<ThreadReference>(new ThreadReference("codex-new")));
        backend.SendTurnAsync(Arg.Any<BackendTurnRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<BackendTurnResult>(new BackendTurnResult("final answer", null)));

        CodexTurnExecutor executor = CreateExecutor(backend, stateStore, sink);
        await executor.ExecuteAsync(CreateTurn("hello"), CancellationToken.None);

        await backend.Received(1).EnsureBackendReadyAsync(Arg.Any<CancellationToken>());
        await backend.Received(1).CreateThreadAsync(SampleContext, Arg.Any<CancellationToken>());
        await backend.DidNotReceive().ResumeThreadAsync(Arg.Any<ThreadReference>(), Arg.Any<CancellationToken>());
        stateStore.ThreadMappings[SampleContext].CurrentThread.Should().Be(new ThreadReference("codex-new"));
        stateStore.ThreadMappings[SampleContext].PreviousThreads.Should().BeEmpty();
        sink.Messages.Should().ContainSingle();
        sink.Messages[0].ChatId.Should().Be(SampleContext.ChatId);
        sink.Messages[0].Text.Should().Be("final answer");
    }

    [Fact]
    public async Task SubsequentTurnShouldResumeStoredCodexThread()
    {
        InMemoryStateStore stateStore = new();
        stateStore.ThreadMappings[SampleContext] = new ThreadMapping(SampleContext, new ThreadReference("codex-existing"));
        RecordingChatReplySink sink = new();
        IBackendClient backend = Substitute.For<IBackendClient>();
        backend.SendTurnAsync(Arg.Any<BackendTurnRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<BackendTurnResult>(new BackendTurnResult("done", null)));

        CodexTurnExecutor executor = CreateExecutor(backend, stateStore, sink);
        await executor.ExecuteAsync(CreateTurn("again"), CancellationToken.None);

        await backend.Received(1).ResumeThreadAsync(new ThreadReference("codex-existing"), Arg.Any<CancellationToken>());
        await backend.DidNotReceive().CreateThreadAsync(Arg.Any<ThreadContext>(), Arg.Any<CancellationToken>());
        stateStore.ThreadMappings[SampleContext].CurrentThread.Should().Be(new ThreadReference("codex-existing"));
        sink.Messages.Should().ContainSingle().Which.Text.Should().Be("done");
    }

    [Fact]
    public async Task RotatedMappingShouldCreateNewCodexThreadAndPreserveHistory()
    {
        InMemoryStateStore stateStore = new();
        stateStore.ThreadMappings[SampleContext] = new ThreadMapping(
            SampleContext,
            null,
            [new ThreadReference("codex-old")]);
        RecordingChatReplySink sink = new();
        IBackendClient backend = Substitute.For<IBackendClient>();
        backend.CreateThreadAsync(SampleContext, Arg.Any<CancellationToken>())
            .Returns(new ValueTask<ThreadReference>(new ThreadReference("codex-fresh")));
        backend.SendTurnAsync(Arg.Any<BackendTurnRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<BackendTurnResult>(new BackendTurnResult("post-clear reply", null)));

        CodexTurnExecutor executor = CreateExecutor(backend, stateStore, sink);
        await executor.ExecuteAsync(CreateTurn("continue"), CancellationToken.None);

        await backend.Received(1).CreateThreadAsync(SampleContext, Arg.Any<CancellationToken>());
        ThreadMapping stored = stateStore.ThreadMappings[SampleContext];
        stored.CurrentThread.Should().Be(new ThreadReference("codex-fresh"));
        stored.PreviousThreads.Should().ContainSingle().Which.Should().Be(new ThreadReference("codex-old"));
        sink.Messages.Should().ContainSingle().Which.Text.Should().Be("post-clear reply");
    }

    [Fact]
    public async Task BackendUnavailableDuringEnsureReadyShouldReplyWithUnavailableMessage()
    {
        InMemoryStateStore stateStore = new();
        RecordingChatReplySink sink = new();
        IBackendClient backend = Substitute.For<IBackendClient>();
        backend.EnsureBackendReadyAsync(Arg.Any<CancellationToken>())
            .Returns(_ => throw new BackendUnavailableException("boom"));

        CodexTurnExecutor executor = CreateExecutor(backend, stateStore, sink);
        await executor.ExecuteAsync(CreateTurn("hi"), CancellationToken.None);

        stateStore.ThreadMappings.Should().BeEmpty();
        sink.Messages.Should().ContainSingle().Which.Text.Should().Be(CodexTurnExecutor.BackendUnavailableReply);
        await backend.DidNotReceive().SendTurnAsync(Arg.Any<BackendTurnRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task BackendUnavailableDuringSendTurnShouldReplyWithUnavailableMessage()
    {
        InMemoryStateStore stateStore = new();
        stateStore.ThreadMappings[SampleContext] = new ThreadMapping(SampleContext, new ThreadReference("codex-x"));
        RecordingChatReplySink sink = new();
        IBackendClient backend = Substitute.For<IBackendClient>();
        backend.SendTurnAsync(Arg.Any<BackendTurnRequest>(), Arg.Any<CancellationToken>())
            .Returns<ValueTask<BackendTurnResult>>(_ => throw new BackendUnavailableException("session dropped"));

        CodexTurnExecutor executor = CreateExecutor(backend, stateStore, sink);
        await executor.ExecuteAsync(CreateTurn("hi"), CancellationToken.None);

        sink.Messages.Should().ContainSingle().Which.Text.Should().Be(CodexTurnExecutor.BackendUnavailableReply);
    }

    [Fact]
    public async Task BackendTurnFailedExceptionShouldReplyWithCodexMessage()
    {
        InMemoryStateStore stateStore = new();
        stateStore.ThreadMappings[SampleContext] = new ThreadMapping(SampleContext, new ThreadReference("codex-x"));
        RecordingChatReplySink sink = new();
        IBackendClient backend = Substitute.For<IBackendClient>();
        backend.SendTurnAsync(Arg.Any<BackendTurnRequest>(), Arg.Any<CancellationToken>())
            .Returns<ValueTask<BackendTurnResult>>(_ => throw new BackendTurnFailedException("rate limited", "failed", "rate_limit"));

        CodexTurnExecutor executor = CreateExecutor(backend, stateStore, sink);
        await executor.ExecuteAsync(CreateTurn("hi"), CancellationToken.None);

        sink.Messages.Should().ContainSingle().Which.Text.Should().Be("The assistant couldn't complete the turn: rate limited");
    }

    [Fact]
    public async Task BackendProtocolExceptionShouldReplyWithGenericMessage()
    {
        InMemoryStateStore stateStore = new();
        stateStore.ThreadMappings[SampleContext] = new ThreadMapping(SampleContext, new ThreadReference("codex-x"));
        RecordingChatReplySink sink = new();
        IBackendClient backend = Substitute.For<IBackendClient>();
        backend.SendTurnAsync(Arg.Any<BackendTurnRequest>(), Arg.Any<CancellationToken>())
            .Returns<ValueTask<BackendTurnResult>>(_ => throw new BackendProtocolException("unexpected frame"));

        CodexTurnExecutor executor = CreateExecutor(backend, stateStore, sink);
        await executor.ExecuteAsync(CreateTurn("hi"), CancellationToken.None);

        sink.Messages.Should().ContainSingle().Which.Text.Should().Be(CodexTurnExecutor.GenericFailureReply);
    }

    [Fact]
    public async Task UnexpectedExceptionShouldReplyWithGenericMessage()
    {
        InMemoryStateStore stateStore = new();
        stateStore.ThreadMappings[SampleContext] = new ThreadMapping(SampleContext, new ThreadReference("codex-x"));
        RecordingChatReplySink sink = new();
        IBackendClient backend = Substitute.For<IBackendClient>();
        backend.SendTurnAsync(Arg.Any<BackendTurnRequest>(), Arg.Any<CancellationToken>())
            .Returns<ValueTask<BackendTurnResult>>(_ => throw new InvalidOperationException("boom"));

        CodexTurnExecutor executor = CreateExecutor(backend, stateStore, sink);
        await executor.ExecuteAsync(CreateTurn("hi"), CancellationToken.None);

        sink.Messages.Should().ContainSingle().Which.Text.Should().Be(CodexTurnExecutor.GenericFailureReply);
    }

    [Fact]
    public async Task ShutdownCancellationShouldPropagateAndNotReply()
    {
        InMemoryStateStore stateStore = new();
        RecordingChatReplySink sink = new();
        IBackendClient backend = Substitute.For<IBackendClient>();
        using CancellationTokenSource cts = new();
        backend.EnsureBackendReadyAsync(Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                cts.Cancel();
                throw new OperationCanceledException(cts.Token);
            });

        CodexTurnExecutor executor = CreateExecutor(backend, stateStore, sink);
        Func<Task> act = async () => await executor.ExecuteAsync(CreateTurn("hi"), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        sink.Messages.Should().BeEmpty();
    }

    [Fact]
    public async Task OperationCanceledOutsideShutdownShouldBeMappedToGenericReply()
    {
        InMemoryStateStore stateStore = new();
        stateStore.ThreadMappings[SampleContext] = new ThreadMapping(SampleContext, new ThreadReference("codex-x"));
        RecordingChatReplySink sink = new();
        IBackendClient backend = Substitute.For<IBackendClient>();
        backend.SendTurnAsync(Arg.Any<BackendTurnRequest>(), Arg.Any<CancellationToken>())
            .Returns<ValueTask<BackendTurnResult>>(_ => throw new OperationCanceledException());

        CodexTurnExecutor executor = CreateExecutor(backend, stateStore, sink);
        await executor.ExecuteAsync(CreateTurn("hi"), CancellationToken.None);

        sink.Messages.Should().ContainSingle().Which.Text.Should().Be(CodexTurnExecutor.GenericFailureReply);
    }

    [Fact]
    public async Task EmptyFinalResponseShouldReplyWithFallbackMessage()
    {
        InMemoryStateStore stateStore = new();
        stateStore.ThreadMappings[SampleContext] = new ThreadMapping(SampleContext, new ThreadReference("codex-x"));
        RecordingChatReplySink sink = new();
        IBackendClient backend = Substitute.For<IBackendClient>();
        backend.SendTurnAsync(Arg.Any<BackendTurnRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<BackendTurnResult>(new BackendTurnResult(null, null)));

        CodexTurnExecutor executor = CreateExecutor(backend, stateStore, sink);
        await executor.ExecuteAsync(CreateTurn("hi"), CancellationToken.None);

        sink.Messages.Should().ContainSingle().Which.Text.Should().Be(CodexTurnExecutor.EmptyResponseReply);
    }

    [Fact]
    public async Task WhitespaceOnlyFinalResponseShouldReplyWithFallbackMessage()
    {
        InMemoryStateStore stateStore = new();
        stateStore.ThreadMappings[SampleContext] = new ThreadMapping(SampleContext, new ThreadReference("codex-x"));
        RecordingChatReplySink sink = new();
        IBackendClient backend = Substitute.For<IBackendClient>();
        backend.SendTurnAsync(Arg.Any<BackendTurnRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<BackendTurnResult>(new BackendTurnResult("   ", null)));

        CodexTurnExecutor executor = CreateExecutor(backend, stateStore, sink);
        await executor.ExecuteAsync(CreateTurn("hi"), CancellationToken.None);

        sink.Messages.Should().ContainSingle().Which.Text.Should().Be(CodexTurnExecutor.EmptyResponseReply);
    }

    [Fact]
    public async Task ApprovalRequestShouldAutoDenyAndReportNotSupported()
    {
        InMemoryStateStore stateStore = new();
        stateStore.ThreadMappings[SampleContext] = new ThreadMapping(SampleContext, new ThreadReference("codex-x"));
        RecordingChatReplySink sink = new();
        IBackendClient backend = Substitute.For<IBackendClient>();
        ApprovalRecord approval = CreateApprovalRecord("approval-1");
        backend.SendTurnAsync(Arg.Any<BackendTurnRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<BackendTurnResult>(new BackendTurnResult(null, approval)));
        backend.ContinueApprovedActionAsync(approval.ApprovalId, ApprovalDecision.Denied, Arg.Any<CancellationToken>())
            .Returns(new ValueTask<BackendTurnResult>(new BackendTurnResult("ignored", null)));

        CodexTurnExecutor executor = CreateExecutor(backend, stateStore, sink);
        await executor.ExecuteAsync(CreateTurn("hi"), CancellationToken.None);

        await backend.Received(1).ContinueApprovedActionAsync(approval.ApprovalId, ApprovalDecision.Denied, Arg.Any<CancellationToken>());
        sink.Messages.Should().ContainSingle().Which.Text.Should().Be(CodexTurnExecutor.ApprovalNotSupportedReply);
    }

    [Fact]
    public async Task ApprovalLoopShouldStopAtMaxIterations()
    {
        InMemoryStateStore stateStore = new();
        stateStore.ThreadMappings[SampleContext] = new ThreadMapping(SampleContext, new ThreadReference("codex-x"));
        RecordingChatReplySink sink = new();
        IBackendClient backend = Substitute.For<IBackendClient>();
        ApprovalRecord approval = CreateApprovalRecord("approval-loop");
        BackendTurnResult approvalResult = new(null, approval);
        backend.SendTurnAsync(Arg.Any<BackendTurnRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<BackendTurnResult>(approvalResult));
        backend.ContinueApprovedActionAsync(approval.ApprovalId, ApprovalDecision.Denied, Arg.Any<CancellationToken>())
            .Returns(new ValueTask<BackendTurnResult>(approvalResult));

        CodexTurnExecutor executor = CreateExecutor(backend, stateStore, sink);
        await executor.ExecuteAsync(CreateTurn("hi"), CancellationToken.None);

        await backend.Received(CodexTurnExecutor.MaxApprovalDenyIterations)
            .ContinueApprovedActionAsync(approval.ApprovalId, ApprovalDecision.Denied, Arg.Any<CancellationToken>());
        sink.Messages.Should().ContainSingle().Which.Text.Should().Be(CodexTurnExecutor.ApprovalNotSupportedReply);
    }

    [Fact]
    public void ConstructorShouldRejectNullDependencies()
    {
        IBackendClient backend = Substitute.For<IBackendClient>();
        InMemoryStateStore stateStore = new();
        RecordingChatReplySink sink = new();
        ILogger<CodexTurnExecutor> noopLogger = NullLogger<CodexTurnExecutor>.Instance;

        Action nullBackend = () => _ = new CodexTurnExecutor(null!, stateStore, sink, noopLogger);
        Action nullStateStore = () => _ = new CodexTurnExecutor(backend, null!, sink, noopLogger);
        Action nullSink = () => _ = new CodexTurnExecutor(backend, stateStore, null!, noopLogger);
        Action nullLogger = () => _ = new CodexTurnExecutor(backend, stateStore, sink, null!);

        nullBackend.Should().Throw<ArgumentNullException>().Which.ParamName.Should().Be("backendClient");
        nullStateStore.Should().Throw<ArgumentNullException>().Which.ParamName.Should().Be("stateStore");
        nullSink.Should().Throw<ArgumentNullException>().Which.ParamName.Should().Be("chatReplySink");
        nullLogger.Should().Throw<ArgumentNullException>().Which.ParamName.Should().Be("logger");
    }

    [Fact]
    public async Task ExecuteAsyncShouldThrowWhenTurnIsNull()
    {
        CodexTurnExecutor executor = CreateExecutor(
            Substitute.For<IBackendClient>(),
            new InMemoryStateStore(),
            new RecordingChatReplySink());

        Func<Task> act = async () => await executor.ExecuteAsync(null!, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    private static CodexTurnExecutor CreateExecutor(
        IBackendClient backend,
        IStateStore stateStore,
        IChatReplySink sink) =>
        new(backend, stateStore, sink, NullLogger<CodexTurnExecutor>.Instance);

    private static QueuedTurn CreateTurn(string text) =>
        new(SampleContext, text, DateTimeOffset.UtcNow);

    private static ApprovalRecord CreateApprovalRecord(string id) =>
        new(
            new ApprovalId(id),
            ApprovalClass.StandardRiskyAction,
            new ApprovalContext(SampleContext.ChatId, SampleContext.Agent, SampleContext.ProjectId, new ThreadReference("codex-x")),
            "Run: ls",
            DateTimeOffset.UtcNow);

    private sealed class RecordingChatReplySink : IChatReplySink
    {
        public List<(ChatId ChatId, string Text)> Messages { get; } = [];

        public ValueTask SendMessageAsync(ChatId chatId, string message, CancellationToken cancellationToken)
        {
            Messages.Add((chatId, message));
            return ValueTask.CompletedTask;
        }
    }
}
