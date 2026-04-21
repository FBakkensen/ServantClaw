using FluentAssertions;
using FluentAssertions.Specialized;
using ServantClaw.Application.Runtime;
using ServantClaw.Domain.Agents;
using ServantClaw.Domain.Common;
using ServantClaw.Domain.Routing;
using ServantClaw.UnitTests.Testing;
using Xunit;

namespace ServantClaw.UnitTests;

public sealed class ThreadMappingCoordinatorTests
{
    [Fact]
    public async Task ResolveShouldCreateAndPersistEmptyMappingWhenMissing()
    {
        InMemoryStateStore stateStore = new();
        ThreadMappingCoordinator coordinator = new(stateStore);
        ThreadContext context = new(new ChatId(100), AgentKind.Coding, new ProjectId("repo"));

        ThreadMapping mapping = await coordinator.ResolveAsync(context, CancellationToken.None);

        mapping.CurrentThread.Should().BeNull();
        mapping.PreviousThreads.Should().BeEmpty();
        stateStore.ThreadMappings[context].Should().Be(mapping);
    }

    [Fact]
    public async Task ResolveShouldReuseExistingMapping()
    {
        InMemoryStateStore stateStore = new();
        ThreadContext context = new(new ChatId(100), AgentKind.Coding, new ProjectId("repo"));
        ThreadMapping existingMapping = new(context, new ThreadReference("thread-1"));
        stateStore.ThreadMappings[context] = existingMapping;
        ThreadMappingCoordinator coordinator = new(stateStore);

        ThreadMapping mapping = await coordinator.ResolveAsync(context, CancellationToken.None);

        mapping.Should().Be(existingMapping);
        stateStore.ThreadMappings[context].CurrentThread.Should().Be(new ThreadReference("thread-1"));
    }

    [Fact]
    public async Task RotateShouldClearCurrentThreadAndPreserveHistory()
    {
        InMemoryStateStore stateStore = new();
        ThreadContext context = new(new ChatId(100), AgentKind.Coding, new ProjectId("repo"));
        stateStore.ThreadMappings[context] = new ThreadMapping(context, new ThreadReference("thread-1"));
        ThreadMappingCoordinator coordinator = new(stateStore);

        ThreadMapping mapping = await coordinator.RotateAsync(context, CancellationToken.None);

        mapping.CurrentThread.Should().BeNull();
        mapping.PreviousThreads.Should().ContainSingle().Which.Should().Be(new ThreadReference("thread-1"));
        stateStore.ThreadMappings[context].Should().Be(mapping);
    }

    [Fact]
    public async Task RotateShouldCreateEmptyMappingWhenNoneExists()
    {
        InMemoryStateStore stateStore = new();
        ThreadContext context = new(new ChatId(200), AgentKind.General, new ProjectId("docs"));
        ThreadMappingCoordinator coordinator = new(stateStore);

        ThreadMapping mapping = await coordinator.RotateAsync(context, CancellationToken.None);

        mapping.CurrentThread.Should().BeNull();
        mapping.PreviousThreads.Should().BeEmpty();
        stateStore.ThreadMappings[context].Should().Be(mapping);
    }

    [Fact]
    public async Task ResolveShouldThrowWhenContextIsNull()
    {
        InMemoryStateStore stateStore = new();
        ThreadMappingCoordinator coordinator = new(stateStore);

        Func<Task> act = async () => await coordinator.ResolveAsync(null!, CancellationToken.None);

        ExceptionAssertions<ArgumentNullException> assertion = await act.Should().ThrowAsync<ArgumentNullException>();
        assertion.Which.ParamName.Should().Be("context");
    }

    [Fact]
    public async Task RotateShouldThrowWhenContextIsNull()
    {
        InMemoryStateStore stateStore = new();
        ThreadMappingCoordinator coordinator = new(stateStore);

        Func<Task> act = async () => await coordinator.RotateAsync(null!, CancellationToken.None);

        ExceptionAssertions<ArgumentNullException> assertion = await act.Should().ThrowAsync<ArgumentNullException>();
        assertion.Which.ParamName.Should().Be("context");
    }

    [Fact]
    public void ConstructorShouldRejectNullStateStore()
    {
        Action act = () => _ = new ThreadMappingCoordinator(null!);

        act.Should().Throw<ArgumentNullException>().Which.ParamName.Should().Be("stateStore");
    }
}
