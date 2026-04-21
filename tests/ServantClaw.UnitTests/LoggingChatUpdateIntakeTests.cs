using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using ServantClaw.Application.Approvals;
using ServantClaw.Application.Commands;
using ServantClaw.Application.Intake.Models;
using ServantClaw.Application.Runtime;
using ServantClaw.Domain.Agents;
using ServantClaw.Domain.Approvals;
using ServantClaw.Domain.Common;
using ServantClaw.Domain.Routing;
using ServantClaw.Domain.State;
using ServantClaw.Infrastructure.Intake;
using ServantClaw.UnitTests.Testing;
using Xunit;

namespace ServantClaw.UnitTests;

public sealed class LoggingChatUpdateIntakeTests
{
    [Fact]
    public async Task TextMessageShouldReplyWithSafeRefusalWhenActiveProjectIsMissing()
    {
        InMemoryStateStore stateStore = new();
        stateStore.ChatStates[100] = new ChatState(new ChatId(100), AgentKind.Coding, new AgentProjectBindings());

        RecordingChatReplySink replySink = new();
        RecordingTurnQueue turnQueue = new();
        FakeProjectCatalog projectCatalog = new(["docs", "repo"]);
        LoggingChatUpdateIntake intake = CreateIntake(stateStore, projectCatalog, replySink, turnQueue);

        await intake.HandleAsync(
            new InboundChatUpdate(
                new ChatId(100),
                new UserId(42),
                "approved-owner",
                DateTimeOffset.UtcNow,
                new InboundChatTextMessage("help me")),
            CancellationToken.None);

        replySink.Messages.Should().ContainSingle();
        replySink.Messages[0].ChatId.Should().Be(new ChatId(100));
        replySink.Messages[0].Text.Should().Be(
            "No active project is selected for agent 'coding'. Use /project <agent-id> <project-id> before sending normal messages. Available projects: docs, repo.");
        turnQueue.EnqueuedTurns.Should().BeEmpty();
    }

    [Fact]
    public async Task TextMessageShouldEnqueueTurnWhenActiveProjectExists()
    {
        InMemoryStateStore stateStore = new();
        stateStore.ChatStates[100] = new ChatState(
            new ChatId(100),
            AgentKind.Coding,
            new AgentProjectBindings(null, new ProjectId("repo")));

        RecordingChatReplySink replySink = new();
        RecordingTurnQueue turnQueue = new();
        LoggingChatUpdateIntake intake = CreateIntake(stateStore, new FakeProjectCatalog(["docs", "repo"]), replySink, turnQueue);

        await intake.HandleAsync(
            new InboundChatUpdate(
                new ChatId(100),
                new UserId(42),
                "approved-owner",
                DateTimeOffset.UtcNow,
                new InboundChatTextMessage("  help me  ")),
            CancellationToken.None);

        replySink.Messages.Should().BeEmpty();
        turnQueue.EnqueuedTurns.Should().ContainSingle();
        QueuedTurn enqueued = turnQueue.EnqueuedTurns[0];
        enqueued.Context.Should().Be(new ThreadContext(new ChatId(100), AgentKind.Coding, new ProjectId("repo")));
        enqueued.MessageText.Should().Be("help me");
    }

    [Fact]
    public async Task TextMessageShouldReplyWithEmptyCatalogHintWhenNoProjectsExist()
    {
        InMemoryStateStore stateStore = new();
        stateStore.ChatStates[100] = new ChatState(new ChatId(100), AgentKind.General, new AgentProjectBindings());

        RecordingChatReplySink replySink = new();
        RecordingTurnQueue turnQueue = new();
        LoggingChatUpdateIntake intake = CreateIntake(stateStore, new FakeProjectCatalog([]), replySink, turnQueue);

        await intake.HandleAsync(
            new InboundChatUpdate(
                new ChatId(100),
                new UserId(42),
                "approved-owner",
                DateTimeOffset.UtcNow,
                new InboundChatTextMessage("help me")),
            CancellationToken.None);

        replySink.Messages.Should().ContainSingle();
        replySink.Messages[0].Text.Should().Be(
            "No active project is selected for agent 'general'. Use /project <agent-id> <project-id> before sending normal messages. No projects are currently available.");
        turnQueue.EnqueuedTurns.Should().BeEmpty();
    }

    private static LoggingChatUpdateIntake CreateIntake(
        IStateStore stateStore,
        IProjectCatalog projectCatalog,
        IChatReplySink replySink,
        IPerContextTurnQueue turnQueue)
    {
        ThreadMappingCoordinator threadMappingCoordinator = new(stateStore);
        return new(
            new ChatCommandProcessor(stateStore, projectCatalog, threadMappingCoordinator, new StubApprovalCoordinator()),
            turnQueue,
            stateStore,
            projectCatalog,
            replySink,
            NullLogger<LoggingChatUpdateIntake>.Instance);
    }

    private sealed class FakeProjectCatalog : IProjectCatalog
    {
        private readonly IReadOnlyCollection<ProjectId> projects;

        public FakeProjectCatalog(IEnumerable<string> projectIds)
        {
            projects = projectIds
                .OrderBy(projectId => projectId, StringComparer.OrdinalIgnoreCase)
                .Select(projectId => new ProjectId(projectId))
                .ToArray();
        }

        public ValueTask<bool> ProjectExistsAsync(ProjectId projectId, CancellationToken cancellationToken) =>
            ValueTask.FromResult(projects.Contains(projectId));

        public ValueTask<IReadOnlyCollection<ProjectId>> ListProjectsAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult(projects);
    }

    private sealed class RecordingChatReplySink : IChatReplySink
    {
        public List<(ChatId ChatId, string Text)> Messages { get; } = [];

        public ValueTask SendMessageAsync(ChatId chatId, string message, CancellationToken cancellationToken)
        {
            Messages.Add((chatId, message));
            return ValueTask.CompletedTask;
        }
    }

    private sealed class RecordingTurnQueue : IPerContextTurnQueue
    {
        public List<QueuedTurn> EnqueuedTurns { get; } = [];

        public ValueTask EnqueueAsync(QueuedTurn turn, CancellationToken cancellationToken)
        {
            EnqueuedTurns.Add(turn);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class StubApprovalCoordinator : IApprovalCoordinator
    {
        public ValueTask<ApprovalDecision> WaitForDecisionAsync(ApprovalRecord record, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public ValueTask<ApprovalResolutionResult> ResolveAsync(
            ApprovalId approvalId,
            ChatId commandChatId,
            ApprovalDecision decision,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
