using FluentAssertions;
using ServantClaw.Application.Runtime;
using ServantClaw.Domain.Agents;
using ServantClaw.Domain.Common;
using ServantClaw.Domain.Routing;
using Xunit;

namespace ServantClaw.UnitTests.Runtime;

public sealed class QueuedTurnTests
{
    private static readonly ThreadContext SampleContext = new(
        new ChatId(42),
        AgentKind.Coding,
        new ProjectId("repo"));

    [Fact]
    public void ConstructorShouldRejectNullContext()
    {
        Action act = () => _ = new QueuedTurn(null!, "hello", DateTimeOffset.UtcNow);

        act.Should().Throw<ArgumentNullException>().Which.ParamName.Should().Be("Context");
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t\r\n ")]
    public void ConstructorShouldRejectBlankMessageText(string text)
    {
        Action act = () => _ = new QueuedTurn(SampleContext, text, DateTimeOffset.UtcNow);

        ArgumentException exception = act.Should().Throw<ArgumentException>().Which;
        exception.ParamName.Should().Be("MessageText");
        exception.Message.Should().Contain("Turn message text must be provided.");
    }

    [Fact]
    public void ConstructorShouldTrimLeadingAndTrailingWhitespace()
    {
        QueuedTurn turn = new(SampleContext, "   hello   ", DateTimeOffset.UtcNow);

        turn.MessageText.Should().Be("hello");
    }

    [Fact]
    public void ConstructorShouldPreserveContextAndTimestamp()
    {
        DateTimeOffset timestamp = DateTimeOffset.UtcNow;

        QueuedTurn turn = new(SampleContext, "hello", timestamp);

        turn.Context.Should().BeSameAs(SampleContext);
        turn.EnqueuedAtUtc.Should().Be(timestamp);
    }
}
