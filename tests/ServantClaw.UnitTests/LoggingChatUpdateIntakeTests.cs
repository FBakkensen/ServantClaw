using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using ServantClaw.Application.Commands;
using ServantClaw.Application.Intake.Models;
using ServantClaw.Domain.Agents;
using ServantClaw.Domain.Approvals;
using ServantClaw.Domain.Common;
using ServantClaw.Domain.Configuration;
using ServantClaw.Domain.Routing;
using ServantClaw.Domain.State;
using ServantClaw.Infrastructure.Intake;
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
        FakeProjectCatalog projectCatalog = new(["docs", "repo"]);
        LoggingChatUpdateIntake intake = CreateIntake(stateStore, projectCatalog, replySink);

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
    }

    [Fact]
    public async Task TextMessageShouldNotReplyWhenActiveProjectExists()
    {
        InMemoryStateStore stateStore = new();
        stateStore.ChatStates[100] = new ChatState(
            new ChatId(100),
            AgentKind.Coding,
            new AgentProjectBindings(null, new ProjectId("repo")));

        RecordingChatReplySink replySink = new();
        LoggingChatUpdateIntake intake = CreateIntake(stateStore, new FakeProjectCatalog(["docs", "repo"]), replySink);

        await intake.HandleAsync(
            new InboundChatUpdate(
                new ChatId(100),
                new UserId(42),
                "approved-owner",
                DateTimeOffset.UtcNow,
                new InboundChatTextMessage("help me")),
            CancellationToken.None);

        replySink.Messages.Should().BeEmpty();
    }

    private static LoggingChatUpdateIntake CreateIntake(
        IStateStore stateStore,
        IProjectCatalog projectCatalog,
        IChatReplySink replySink) =>
        new(
            new ChatCommandProcessor(stateStore, projectCatalog),
            stateStore,
            projectCatalog,
            replySink,
            NullLogger<LoggingChatUpdateIntake>.Instance);

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

    private sealed class InMemoryStateStore : IStateStore
    {
        public Dictionary<long, ChatState> ChatStates { get; } = [];

        public ValueTask<ChatState?> GetChatStateAsync(ChatId chatId, CancellationToken cancellationToken) =>
            ValueTask.FromResult(ChatStates.TryGetValue(chatId.Value, out ChatState? state) ? state : null);

        public ValueTask SaveChatStateAsync(ChatState chatState, CancellationToken cancellationToken)
        {
            ChatStates[chatState.ChatId.Value] = chatState;
            return ValueTask.CompletedTask;
        }

        public ValueTask<ThreadMapping?> GetThreadMappingAsync(ThreadContext context, CancellationToken cancellationToken) =>
            ValueTask.FromResult<ThreadMapping?>(null);

        public ValueTask SaveThreadMappingAsync(ThreadMapping threadMapping, CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;

        public ValueTask<ApprovalRecord?> GetApprovalAsync(ApprovalId approvalId, CancellationToken cancellationToken) =>
            ValueTask.FromResult<ApprovalRecord?>(null);

        public ValueTask<IReadOnlyCollection<ApprovalRecord>> GetPendingApprovalsAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyCollection<ApprovalRecord>>([]);

        public ValueTask SaveApprovalAsync(ApprovalRecord approvalRecord, CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;

        public ValueTask<OwnerConfiguration?> GetOwnerConfigurationAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult<OwnerConfiguration?>(null);
    }
}
