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

public sealed class NoOpTurnExecutorTests
{
    [Fact]
    public async Task ExecuteAsyncShouldCompleteWithoutThrowing()
    {
        NoOpTurnExecutor executor = new(NullLogger<NoOpTurnExecutor>.Instance);
        QueuedTurn turn = new(
            new ThreadContext(new ChatId(100), AgentKind.Coding, new ProjectId("project")),
            "hello",
            DateTimeOffset.UtcNow);

        Func<Task> act = async () => await executor.ExecuteAsync(turn, CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task ExecuteAsyncShouldLogWarningThatNoExecutorIsWired()
    {
        ILogger<NoOpTurnExecutor> logger = Substitute.For<ILogger<NoOpTurnExecutor>>();
        logger.IsEnabled(LogLevel.Warning).Returns(true);
        NoOpTurnExecutor executor = new(logger);
        QueuedTurn turn = new(
            new ThreadContext(new ChatId(100), AgentKind.Coding, new ProjectId("project")),
            "hello",
            DateTimeOffset.UtcNow);

        await executor.ExecuteAsync(turn, CancellationToken.None);

        logger.Received(1).Log(
            LogLevel.Warning,
            Arg.Any<EventId>(),
            Arg.Any<object>(),
            Arg.Any<Exception?>(),
            Arg.Any<Func<object, Exception?, string>>());
    }

    [Fact]
    public async Task ExecuteAsyncShouldRejectNullTurn()
    {
        NoOpTurnExecutor executor = new(NullLogger<NoOpTurnExecutor>.Instance);

        Func<Task> act = async () => await executor.ExecuteAsync(null!, CancellationToken.None);

        (await act.Should().ThrowAsync<ArgumentNullException>()).Which.ParamName.Should().Be("turn");
    }

    [Fact]
    public void ConstructorShouldRejectNullLogger()
    {
        Action act = () => _ = new NoOpTurnExecutor(null!);

        act.Should().Throw<ArgumentNullException>().Which.ParamName.Should().Be("logger");
    }
}
