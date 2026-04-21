using FluentAssertions;
using ServantClaw.Application.Approvals;
using ServantClaw.Application.Commands;
using ServantClaw.Application.Intake.Models;
using ServantClaw.Application.Runtime;
using ServantClaw.Domain.Agents;
using ServantClaw.Domain.Approvals;
using ServantClaw.Domain.Common;
using ServantClaw.Domain.Routing;
using ServantClaw.UnitTests.Testing;
using Xunit;

namespace ServantClaw.UnitTests;

public sealed class ChatCommandProcessorTests
{
    [Fact]
    public async Task AgentCommandShouldPersistActiveAgentForNewChat()
    {
        InMemoryStateStore stateStore = new();
        ChatCommandProcessor processor = CreateProcessor(stateStore, new FakeProjectCatalog());
        InboundChatUpdate update = CreateUpdate(new InboundChatCommand("agent", ["coding"], "/agent coding"));

        ChatCommandResult result = await processor.ProcessAsync(update, CancellationToken.None);

        result.ResponseText.Should().Be("Active agent set to 'coding'.");
        stateStore.ChatStates.Should().ContainKey(100);
        stateStore.ChatStates[100].ActiveAgent.Should().Be(AgentKind.Coding);
    }

    [Fact]
    public async Task AgentCommandShouldSetGeneralAgent()
    {
        InMemoryStateStore stateStore = new();
        stateStore.ChatStates[100] = new ChatState(
            new ChatId(100),
            AgentKind.Coding,
            new AgentProjectBindings(null, new ProjectId("repo")));
        ChatCommandProcessor processor = CreateProcessor(stateStore, new FakeProjectCatalog());
        InboundChatUpdate update = CreateUpdate(new InboundChatCommand("agent", ["general"], "/agent general"));

        ChatCommandResult result = await processor.ProcessAsync(update, CancellationToken.None);

        result.ResponseText.Should().Be("Active agent set to 'general'.");
        stateStore.ChatStates[100].ActiveAgent.Should().Be(AgentKind.General);
    }

    [Fact]
    public async Task AgentCommandShouldRejectInvalidAgentWithoutChangingState()
    {
        InMemoryStateStore stateStore = new();
        ChatCommandProcessor processor = CreateProcessor(stateStore, new FakeProjectCatalog());
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
        ChatCommandProcessor processor = CreateProcessor(stateStore, projectCatalog);
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

        ChatCommandProcessor processor = CreateProcessor(stateStore, new FakeProjectCatalog());
        InboundChatUpdate update = CreateUpdate(new InboundChatCommand("project", ["coding", "missing"], "/project coding missing"));

        ChatCommandResult result = await processor.ProcessAsync(update, CancellationToken.None);

        result.ResponseText.Should().Be("Unknown project 'missing'.");
        stateStore.ChatStates[100].Should().Be(existingState);
    }

    [Fact]
    public async Task ClearCommandShouldRotateCurrentThreadAndPreserveHistory()
    {
        InMemoryStateStore stateStore = new();
        stateStore.ChatStates[100] = new ChatState(
            new ChatId(100),
            AgentKind.Coding,
            new AgentProjectBindings(null, new ProjectId("repo")));

        ThreadContext context = new(new ChatId(100), AgentKind.Coding, new ProjectId("repo"));
        stateStore.ThreadMappings[context] = new ThreadMapping(context, new ThreadReference("thread-1"));
        ChatCommandProcessor processor = CreateProcessor(stateStore, new FakeProjectCatalog(["repo"]));

        ChatCommandResult result = await processor.ProcessAsync(
            CreateUpdate(new InboundChatCommand("clear", [], "/clear")),
            CancellationToken.None);

        result.ResponseText.Should().Be("Started a fresh thread for agent 'coding' and project 'repo'.");
        stateStore.ThreadMappings[context].CurrentThread.Should().BeNull();
        stateStore.ThreadMappings[context].PreviousThreads.Should().ContainSingle().Which.Should().Be(new ThreadReference("thread-1"));
    }

    [Fact]
    public async Task ClearCommandShouldRefuseWhenActiveProjectIsMissing()
    {
        InMemoryStateStore stateStore = new();
        stateStore.ChatStates[100] = new ChatState(new ChatId(100), AgentKind.Coding, new AgentProjectBindings());
        ChatCommandProcessor processor = CreateProcessor(stateStore, new FakeProjectCatalog(["docs", "repo"]));

        ChatCommandResult result = await processor.ProcessAsync(
            CreateUpdate(new InboundChatCommand("clear", [], "/clear")),
            CancellationToken.None);

        result.ResponseText.Should().Be(
            "No active project is selected for agent 'coding'. Use /project <agent-id> <project-id> before sending normal messages. Available projects: docs, repo.");
        stateStore.ThreadMappings.Should().BeEmpty();
    }

    [Fact]
    public async Task ProcessAsyncShouldRejectUnknownCommand()
    {
        InMemoryStateStore stateStore = new();
        ChatCommandProcessor processor = CreateProcessor(stateStore, new FakeProjectCatalog());
        InboundChatUpdate update = CreateUpdate(new InboundChatCommand("whoami", [], "/whoami"));

        ChatCommandResult result = await processor.ProcessAsync(update, CancellationToken.None);

        result.ResponseText.Should().Contain("whoami");
        result.ResponseText.Should().ContainAny("Unsupported", "Unknown", "unsupported", "unknown");
        stateStore.ChatStates.Should().BeEmpty();
        stateStore.ThreadMappings.Should().BeEmpty();
    }

    [Fact]
    public async Task AgentCommandShouldReturnUsageWhenArgumentsMissing()
    {
        InMemoryStateStore stateStore = new();
        ChatCommandProcessor processor = CreateProcessor(stateStore, new FakeProjectCatalog());
        InboundChatUpdate update = CreateUpdate(new InboundChatCommand("agent", [], "/agent"));

        ChatCommandResult result = await processor.ProcessAsync(update, CancellationToken.None);

        result.ResponseText.Should().Contain("/agent");
        stateStore.ChatStates.Should().BeEmpty();
    }

    [Fact]
    public async Task AgentCommandShouldReturnUsageWhenTooManyArguments()
    {
        InMemoryStateStore stateStore = new();
        ChatCommandProcessor processor = CreateProcessor(stateStore, new FakeProjectCatalog());
        InboundChatUpdate update = CreateUpdate(new InboundChatCommand("agent", ["coding", "extra"], "/agent coding extra"));

        ChatCommandResult result = await processor.ProcessAsync(update, CancellationToken.None);

        result.ResponseText.Should().Contain("/agent");
        stateStore.ChatStates.Should().BeEmpty();
    }

    [Fact]
    public async Task AgentCommandShouldPreserveExistingProjectBindings()
    {
        InMemoryStateStore stateStore = new();
        stateStore.ChatStates[100] = new ChatState(
            new ChatId(100),
            AgentKind.General,
            new AgentProjectBindings(new ProjectId("docs"), new ProjectId("repo")));

        ChatCommandProcessor processor = CreateProcessor(stateStore, new FakeProjectCatalog());
        InboundChatUpdate update = CreateUpdate(new InboundChatCommand("agent", ["coding"], "/agent coding"));

        ChatCommandResult result = await processor.ProcessAsync(update, CancellationToken.None);

        result.ResponseText.Should().Be("Active agent set to 'coding'.");
        ChatState savedState = stateStore.ChatStates[100];
        savedState.ActiveAgent.Should().Be(AgentKind.Coding);
        savedState.ProjectBindings.GeneralProjectId.Should().Be(new ProjectId("docs"));
        savedState.ProjectBindings.CodingProjectId.Should().Be(new ProjectId("repo"));
    }

    [Fact]
    public async Task ProjectCommandShouldReturnUsageWhenArgumentsMissing()
    {
        InMemoryStateStore stateStore = new();
        ChatCommandProcessor processor = CreateProcessor(stateStore, new FakeProjectCatalog(["repo"]));
        InboundChatUpdate update = CreateUpdate(new InboundChatCommand("project", ["coding"], "/project coding"));

        ChatCommandResult result = await processor.ProcessAsync(update, CancellationToken.None);

        result.ResponseText.Should().Contain("/project");
        stateStore.ChatStates.Should().BeEmpty();
    }

    [Fact]
    public async Task ProjectCommandShouldReturnUsageWhenTooManyArguments()
    {
        InMemoryStateStore stateStore = new();
        ChatCommandProcessor processor = CreateProcessor(stateStore, new FakeProjectCatalog(["repo"]));
        InboundChatUpdate update = CreateUpdate(
            new InboundChatCommand("project", ["coding", "repo", "extra"], "/project coding repo extra"));

        ChatCommandResult result = await processor.ProcessAsync(update, CancellationToken.None);

        result.ResponseText.Should().Contain("/project");
        stateStore.ChatStates.Should().BeEmpty();
    }

    [Fact]
    public async Task ProjectCommandShouldRejectBlankProjectId()
    {
        InMemoryStateStore stateStore = new();
        FakeProjectCatalog projectCatalog = new(["repo"]);
        ChatCommandProcessor processor = CreateProcessor(stateStore, projectCatalog);
        InboundChatUpdate update = CreateUpdate(new InboundChatCommand("project", ["coding", " "], "/project coding  "));

        ChatCommandResult result = await processor.ProcessAsync(update, CancellationToken.None);

        result.ResponseText.Should().Be("Project id must be provided.");
        stateStore.ChatStates.Should().BeEmpty();
    }

    [Fact]
    public async Task ProjectCommandShouldRejectInvalidAgentWithoutChangingState()
    {
        InMemoryStateStore stateStore = new();
        FakeProjectCatalog projectCatalog = new(["repo"]);
        ChatCommandProcessor processor = CreateProcessor(stateStore, projectCatalog);
        InboundChatUpdate update = CreateUpdate(new InboundChatCommand("project", ["writer", "repo"], "/project writer repo"));

        ChatCommandResult result = await processor.ProcessAsync(update, CancellationToken.None);

        result.ResponseText.Should().Be("Invalid agent id. Supported values: general, coding.");
        stateStore.ChatStates.Should().BeEmpty();
    }

    [Fact]
    public void ConstructorShouldRejectNullStateStore()
    {
        Action act = () => _ = new ChatCommandProcessor(
            null!,
            new FakeProjectCatalog(),
            new ThreadMappingCoordinator(new InMemoryStateStore()),
            new StubApprovalCoordinator());

        act.Should().Throw<ArgumentNullException>().Which.ParamName.Should().Be("stateStore");
    }

    [Fact]
    public void ConstructorShouldRejectNullProjectCatalog()
    {
        InMemoryStateStore stateStore = new();
        Action act = () => _ = new ChatCommandProcessor(
            stateStore,
            null!,
            new ThreadMappingCoordinator(stateStore),
            new StubApprovalCoordinator());

        act.Should().Throw<ArgumentNullException>().Which.ParamName.Should().Be("projectCatalog");
    }

    [Fact]
    public void ConstructorShouldRejectNullThreadMappingCoordinator()
    {
        Action act = () => _ = new ChatCommandProcessor(
            new InMemoryStateStore(),
            new FakeProjectCatalog(),
            null!,
            new StubApprovalCoordinator());

        act.Should().Throw<ArgumentNullException>().Which.ParamName.Should().Be("threadMappingCoordinator");
    }

    [Fact]
    public void ConstructorShouldRejectNullApprovalCoordinator()
    {
        InMemoryStateStore stateStore = new();
        Action act = () => _ = new ChatCommandProcessor(
            stateStore,
            new FakeProjectCatalog(),
            new ThreadMappingCoordinator(stateStore),
            null!);

        act.Should().Throw<ArgumentNullException>().Which.ParamName.Should().Be("approvalCoordinator");
    }

    [Fact]
    public async Task ApproveCommandShouldDelegateToCoordinatorAndReturnAckMessage()
    {
        InMemoryStateStore stateStore = new();
        StubApprovalCoordinator coordinator = new();
        coordinator.ResolutionResults.Enqueue(new ApprovalResolutionResult(
            ApprovalResolutionOutcome.Resolved,
            "Approval 'A1' accepted. The assistant is resuming the turn."));
        ChatCommandProcessor processor = CreateProcessor(stateStore, new FakeProjectCatalog(), coordinator);

        ChatCommandResult result = await processor.ProcessAsync(
            CreateUpdate(new InboundChatCommand("approve", ["A1"], "/approve A1")),
            CancellationToken.None);

        result.ResponseText.Should().Be("Approval 'A1' accepted. The assistant is resuming the turn.");
        coordinator.ResolveCalls.Should().ContainSingle();
        coordinator.ResolveCalls[0].ApprovalId.Should().Be(new ApprovalId("A1"));
        coordinator.ResolveCalls[0].ChatId.Should().Be(new ChatId(100));
        coordinator.ResolveCalls[0].Decision.Should().Be(ApprovalDecision.Approved);
    }

    [Fact]
    public async Task DenyCommandShouldDelegateToCoordinatorWithDeniedDecision()
    {
        InMemoryStateStore stateStore = new();
        StubApprovalCoordinator coordinator = new();
        coordinator.ResolutionResults.Enqueue(new ApprovalResolutionResult(
            ApprovalResolutionOutcome.Resolved,
            "Approval 'A1' denied."));
        ChatCommandProcessor processor = CreateProcessor(stateStore, new FakeProjectCatalog(), coordinator);

        ChatCommandResult result = await processor.ProcessAsync(
            CreateUpdate(new InboundChatCommand("deny", ["A1"], "/deny A1")),
            CancellationToken.None);

        result.ResponseText.Should().Be("Approval 'A1' denied.");
        coordinator.ResolveCalls.Should().ContainSingle().Which.Decision.Should().Be(ApprovalDecision.Denied);
    }

    [Fact]
    public async Task ApproveCommandShouldReturnUsageWhenArgumentsMissing()
    {
        InMemoryStateStore stateStore = new();
        StubApprovalCoordinator coordinator = new();
        ChatCommandProcessor processor = CreateProcessor(stateStore, new FakeProjectCatalog(), coordinator);

        ChatCommandResult result = await processor.ProcessAsync(
            CreateUpdate(new InboundChatCommand("approve", [], "/approve")),
            CancellationToken.None);

        result.ResponseText.Should().Be("Usage: /approve <approval-id>");
        coordinator.ResolveCalls.Should().BeEmpty();
    }

    [Fact]
    public async Task ApproveCommandShouldReturnUsageWhenTooManyArguments()
    {
        InMemoryStateStore stateStore = new();
        StubApprovalCoordinator coordinator = new();
        ChatCommandProcessor processor = CreateProcessor(stateStore, new FakeProjectCatalog(), coordinator);

        ChatCommandResult result = await processor.ProcessAsync(
            CreateUpdate(new InboundChatCommand("approve", ["A1", "extra"], "/approve A1 extra")),
            CancellationToken.None);

        result.ResponseText.Should().Be("Usage: /approve <approval-id>");
        coordinator.ResolveCalls.Should().BeEmpty();
    }

    [Fact]
    public async Task DenyCommandShouldReturnUsageWhenArgumentsMissing()
    {
        InMemoryStateStore stateStore = new();
        StubApprovalCoordinator coordinator = new();
        ChatCommandProcessor processor = CreateProcessor(stateStore, new FakeProjectCatalog(), coordinator);

        ChatCommandResult result = await processor.ProcessAsync(
            CreateUpdate(new InboundChatCommand("deny", [], "/deny")),
            CancellationToken.None);

        result.ResponseText.Should().Be("Usage: /deny <approval-id>");
        coordinator.ResolveCalls.Should().BeEmpty();
    }

    [Fact]
    public async Task ApproveCommandShouldPropagateUnknownOutcomeMessage()
    {
        InMemoryStateStore stateStore = new();
        StubApprovalCoordinator coordinator = new();
        coordinator.ResolutionResults.Enqueue(new ApprovalResolutionResult(
            ApprovalResolutionOutcome.UnknownId,
            "Approval 'missing' was not found."));
        ChatCommandProcessor processor = CreateProcessor(stateStore, new FakeProjectCatalog(), coordinator);

        ChatCommandResult result = await processor.ProcessAsync(
            CreateUpdate(new InboundChatCommand("approve", ["missing"], "/approve missing")),
            CancellationToken.None);

        result.ResponseText.Should().Be("Approval 'missing' was not found.");
    }

    [Fact]
    public async Task ClearCommandShouldReportNoProjectsAvailableWhenCatalogIsEmpty()
    {
        InMemoryStateStore stateStore = new();
        stateStore.ChatStates[100] = new ChatState(new ChatId(100), AgentKind.Coding, new AgentProjectBindings());
        ChatCommandProcessor processor = CreateProcessor(stateStore, new FakeProjectCatalog());

        ChatCommandResult result = await processor.ProcessAsync(
            CreateUpdate(new InboundChatCommand("clear", [], "/clear")),
            CancellationToken.None);

        result.ResponseText.Should().Contain("No projects are currently available");
        stateStore.ThreadMappings.Should().BeEmpty();
    }

    [Fact]
    public async Task ClearCommandShouldReturnUsageWhenArgumentsProvided()
    {
        InMemoryStateStore stateStore = new();
        stateStore.ChatStates[100] = new ChatState(
            new ChatId(100),
            AgentKind.Coding,
            new AgentProjectBindings(null, new ProjectId("repo")));

        ChatCommandProcessor processor = CreateProcessor(stateStore, new FakeProjectCatalog(["repo"]));
        InboundChatUpdate update = CreateUpdate(new InboundChatCommand("clear", ["extra"], "/clear extra"));

        ChatCommandResult result = await processor.ProcessAsync(update, CancellationToken.None);

        result.ResponseText.Should().Contain("/clear");
        stateStore.ThreadMappings.Should().BeEmpty();
    }

    private static InboundChatUpdate CreateUpdate(InboundChatInput input) =>
        new(new ChatId(100), new UserId(42), "approved-owner", DateTimeOffset.UtcNow, input);

    private static ChatCommandProcessor CreateProcessor(
        InMemoryStateStore stateStore,
        FakeProjectCatalog projectCatalog,
        IApprovalCoordinator? approvalCoordinator = null) =>
        new(
            stateStore,
            projectCatalog,
            new ThreadMappingCoordinator(stateStore),
            approvalCoordinator ?? new StubApprovalCoordinator());

    private sealed class FakeProjectCatalog : IProjectCatalog
    {
        public FakeProjectCatalog()
        {
        }

        public FakeProjectCatalog(IEnumerable<string> existingProjects)
        {
            foreach (string existingProject in existingProjects)
            {
                ExistingProjects.Add(existingProject);
            }
        }

        public HashSet<string> ExistingProjects { get; } = [];

        public ValueTask<bool> ProjectExistsAsync(ProjectId projectId, CancellationToken cancellationToken) =>
            ValueTask.FromResult(ExistingProjects.Contains(projectId.Value));

        public ValueTask<IReadOnlyCollection<ProjectId>> ListProjectsAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyCollection<ProjectId>>(
                ExistingProjects
                    .OrderBy(projectId => projectId, StringComparer.OrdinalIgnoreCase)
                    .Select(projectId => new ProjectId(projectId))
                    .ToArray());
    }

    private sealed class StubApprovalCoordinator : IApprovalCoordinator
    {
        public Queue<ApprovalResolutionResult> ResolutionResults { get; } = new();

        public List<(ApprovalId ApprovalId, ChatId ChatId, ApprovalDecision Decision)> ResolveCalls { get; } = [];

        public ValueTask<ApprovalDecision> WaitForDecisionAsync(ApprovalRecord record, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public ValueTask<ApprovalResolutionResult> ResolveAsync(
            ApprovalId approvalId,
            ChatId commandChatId,
            ApprovalDecision decision,
            CancellationToken cancellationToken)
        {
            ResolveCalls.Add((approvalId, commandChatId, decision));
            if (ResolutionResults.Count == 0)
            {
                throw new InvalidOperationException("No queued resolution results for stub coordinator.");
            }

            return ValueTask.FromResult(ResolutionResults.Dequeue());
        }
    }
}
