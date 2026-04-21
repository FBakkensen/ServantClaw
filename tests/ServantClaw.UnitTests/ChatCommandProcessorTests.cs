using FluentAssertions;
using ServantClaw.Application.Commands;
using ServantClaw.Application.Intake.Models;
using ServantClaw.Domain.Agents;
using ServantClaw.Domain.Approvals;
using ServantClaw.Domain.Common;
using ServantClaw.Domain.Configuration;
using ServantClaw.Domain.Routing;
using ServantClaw.Domain.State;
using Xunit;

namespace ServantClaw.UnitTests;

public sealed class ChatCommandProcessorTests
{
    [Fact]
    public async Task AgentCommandShouldPersistActiveAgentForNewChat()
    {
        InMemoryStateStore stateStore = new();
        ChatCommandProcessor processor = new(stateStore, new FakeProjectCatalog());
        InboundChatUpdate update = CreateUpdate(new InboundChatCommand("agent", ["coding"], "/agent coding"));

        ChatCommandResult result = await processor.ProcessAsync(update, CancellationToken.None);

        result.ResponseText.Should().Be("Active agent set to 'coding'.");
        stateStore.ChatStates.Should().ContainKey(100);
        stateStore.ChatStates[100].ActiveAgent.Should().Be(AgentKind.Coding);
    }

    [Fact]
    public async Task AgentCommandShouldRejectInvalidAgentWithoutChangingState()
    {
        InMemoryStateStore stateStore = new();
        ChatCommandProcessor processor = new(stateStore, new FakeProjectCatalog());
        InboundChatUpdate update = CreateUpdate(new InboundChatCommand("agent", ["writer"], "/agent writer"));

        ChatCommandResult result = await processor.ProcessAsync(update, CancellationToken.None);

        result.ResponseText.Should().Be("Invalid agent id. Supported values: general, coding.");
        stateStore.ChatStates.Should().BeEmpty();
    }

    [Fact]
    public async Task ProjectCommandShouldBindSelectedAgentAndPreserveOtherBinding()
    {
        InMemoryStateStore stateStore = new();
        stateStore.ChatStates[100] = new ChatState(
            new ChatId(100),
            AgentKind.General,
            new AgentProjectBindings(new ProjectId("docs"), null));

        FakeProjectCatalog projectCatalog = new();
        projectCatalog.ExistingProjects.Add("repo");
        ChatCommandProcessor processor = new(stateStore, projectCatalog);
        InboundChatUpdate update = CreateUpdate(new InboundChatCommand("project", ["coding", "repo"], "/project coding repo"));

        ChatCommandResult result = await processor.ProcessAsync(update, CancellationToken.None);

        result.ResponseText.Should().Be("Active agent set to 'coding'. Active project set to 'repo'.");
        ChatState savedState = stateStore.ChatStates[100];
        savedState.ActiveAgent.Should().Be(AgentKind.Coding);
        savedState.ProjectBindings.GeneralProjectId.Should().Be(new ProjectId("docs"));
        savedState.ProjectBindings.CodingProjectId.Should().Be(new ProjectId("repo"));
    }

    [Fact]
    public async Task ProjectCommandShouldRejectUnknownProjectWithoutChangingState()
    {
        InMemoryStateStore stateStore = new();
        ChatState existingState = new(
            new ChatId(100),
            AgentKind.General,
            new AgentProjectBindings(new ProjectId("docs"), null));
        stateStore.ChatStates[100] = existingState;

        ChatCommandProcessor processor = new(stateStore, new FakeProjectCatalog());
        InboundChatUpdate update = CreateUpdate(new InboundChatCommand("project", ["coding", "missing"], "/project coding missing"));

        ChatCommandResult result = await processor.ProcessAsync(update, CancellationToken.None);

        result.ResponseText.Should().Be("Unknown project 'missing'.");
        stateStore.ChatStates[100].Should().Be(existingState);
    }

    private static InboundChatUpdate CreateUpdate(InboundChatInput input) =>
        new(new ChatId(100), new UserId(42), "approved-owner", DateTimeOffset.UtcNow, input);

    private sealed class FakeProjectCatalog : IProjectCatalog
    {
        public HashSet<string> ExistingProjects { get; } = [];

        public ValueTask<bool> ProjectExistsAsync(ProjectId projectId, CancellationToken cancellationToken) =>
            ValueTask.FromResult(ExistingProjects.Contains(projectId.Value));
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
