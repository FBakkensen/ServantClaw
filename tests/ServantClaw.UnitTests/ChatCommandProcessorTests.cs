using FluentAssertions;
using ServantClaw.Application.Commands;
using ServantClaw.Application.Intake.Models;
using ServantClaw.Application.Runtime;
using ServantClaw.Domain.Agents;
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
        ChatCommandProcessor processor = CreateProcessor(stateStore, new FakeProjectCatalog(), ["thread-1"]);
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
        ChatCommandProcessor processor = CreateProcessor(stateStore, new FakeProjectCatalog(), ["thread-1"]);
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
        ChatCommandProcessor processor = CreateProcessor(stateStore, projectCatalog, ["thread-1"]);
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

        ChatCommandProcessor processor = CreateProcessor(stateStore, new FakeProjectCatalog(), ["thread-1"]);
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
        ChatCommandProcessor processor = CreateProcessor(stateStore, new FakeProjectCatalog(["repo"]), ["thread-2"]);

        ChatCommandResult result = await processor.ProcessAsync(
            CreateUpdate(new InboundChatCommand("clear", [], "/clear")),
            CancellationToken.None);

        result.ResponseText.Should().Be("Started a fresh thread for agent 'coding' and project 'repo'.");
        stateStore.ThreadMappings[context].CurrentThread.Should().Be(new ThreadReference("thread-2"));
        stateStore.ThreadMappings[context].PreviousThreads.Should().ContainSingle().Which.Should().Be(new ThreadReference("thread-1"));
    }

    [Fact]
    public async Task ClearCommandShouldRefuseWhenActiveProjectIsMissing()
    {
        InMemoryStateStore stateStore = new();
        stateStore.ChatStates[100] = new ChatState(new ChatId(100), AgentKind.Coding, new AgentProjectBindings());
        ChatCommandProcessor processor = CreateProcessor(stateStore, new FakeProjectCatalog(["docs", "repo"]), ["thread-1"]);

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
        ChatCommandProcessor processor = CreateProcessor(stateStore, new FakeProjectCatalog(), ["thread-1"]);
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
        ChatCommandProcessor processor = CreateProcessor(stateStore, new FakeProjectCatalog(), ["thread-1"]);
        InboundChatUpdate update = CreateUpdate(new InboundChatCommand("agent", [], "/agent"));

        ChatCommandResult result = await processor.ProcessAsync(update, CancellationToken.None);

        result.ResponseText.Should().Contain("/agent");
        stateStore.ChatStates.Should().BeEmpty();
    }

    [Fact]
    public async Task AgentCommandShouldReturnUsageWhenTooManyArguments()
    {
        InMemoryStateStore stateStore = new();
        ChatCommandProcessor processor = CreateProcessor(stateStore, new FakeProjectCatalog(), ["thread-1"]);
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

        ChatCommandProcessor processor = CreateProcessor(stateStore, new FakeProjectCatalog(), ["thread-1"]);
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
        ChatCommandProcessor processor = CreateProcessor(stateStore, new FakeProjectCatalog(["repo"]), ["thread-1"]);
        InboundChatUpdate update = CreateUpdate(new InboundChatCommand("project", ["coding"], "/project coding"));

        ChatCommandResult result = await processor.ProcessAsync(update, CancellationToken.None);

        result.ResponseText.Should().Contain("/project");
        stateStore.ChatStates.Should().BeEmpty();
    }

    [Fact]
    public async Task ProjectCommandShouldReturnUsageWhenTooManyArguments()
    {
        InMemoryStateStore stateStore = new();
        ChatCommandProcessor processor = CreateProcessor(stateStore, new FakeProjectCatalog(["repo"]), ["thread-1"]);
        InboundChatUpdate update = CreateUpdate(
            new InboundChatCommand("project", ["coding", "repo", "extra"], "/project coding repo extra"));

        ChatCommandResult result = await processor.ProcessAsync(update, CancellationToken.None);

        result.ResponseText.Should().Contain("/project");
        stateStore.ChatStates.Should().BeEmpty();
    }

    [Fact]
    public async Task ProjectCommandShouldRejectInvalidAgentWithoutChangingState()
    {
        InMemoryStateStore stateStore = new();
        FakeProjectCatalog projectCatalog = new(["repo"]);
        ChatCommandProcessor processor = CreateProcessor(stateStore, projectCatalog, ["thread-1"]);
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
            new ThreadMappingCoordinator(new InMemoryStateStore(), new FixedThreadReferenceGenerator(["thread-1"])));

        act.Should().Throw<ArgumentNullException>().Which.ParamName.Should().Be("stateStore");
    }

    [Fact]
    public void ConstructorShouldRejectNullProjectCatalog()
    {
        InMemoryStateStore stateStore = new();
        Action act = () => _ = new ChatCommandProcessor(
            stateStore,
            null!,
            new ThreadMappingCoordinator(stateStore, new FixedThreadReferenceGenerator(["thread-1"])));

        act.Should().Throw<ArgumentNullException>().Which.ParamName.Should().Be("projectCatalog");
    }

    [Fact]
    public void ConstructorShouldRejectNullThreadMappingCoordinator()
    {
        Action act = () => _ = new ChatCommandProcessor(new InMemoryStateStore(), new FakeProjectCatalog(), null!);

        act.Should().Throw<ArgumentNullException>().Which.ParamName.Should().Be("threadMappingCoordinator");
    }

    [Fact]
    public async Task ClearCommandShouldReportNoProjectsAvailableWhenCatalogIsEmpty()
    {
        InMemoryStateStore stateStore = new();
        stateStore.ChatStates[100] = new ChatState(new ChatId(100), AgentKind.Coding, new AgentProjectBindings());
        ChatCommandProcessor processor = CreateProcessor(stateStore, new FakeProjectCatalog(), ["thread-1"]);

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

        ChatCommandProcessor processor = CreateProcessor(stateStore, new FakeProjectCatalog(["repo"]), ["thread-2"]);
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
        IEnumerable<string> threadValues) =>
        new(
            stateStore,
            projectCatalog,
            new ThreadMappingCoordinator(stateStore, new FixedThreadReferenceGenerator(threadValues)));

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
}
