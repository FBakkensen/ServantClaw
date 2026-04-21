using FluentAssertions;
using ServantClaw.Domain.Agents;
using ServantClaw.Domain.Common;
using ServantClaw.Domain.Routing;
using ServantClaw.Domain.Runtime;
using Xunit;

namespace ServantClaw.UnitTests.Runtime;

public sealed class BackendTurnRequestTests
{
    private static readonly ThreadContext SampleContext = new(
        new ChatId(42),
        AgentKind.Coding,
        new ProjectId("repo"));

    [Fact]
    public void ConstructorShouldRejectNullContext()
    {
        Action act = () => _ = new BackendTurnRequest(null!, "hello");

        act.Should().Throw<ArgumentNullException>().Which.ParamName.Should().Be("Context");
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t\n  ")]
    public void ConstructorShouldRejectBlankMessage(string message)
    {
        Action act = () => _ = new BackendTurnRequest(SampleContext, message);

        ArgumentException exception = act.Should().Throw<ArgumentException>().Which;
        exception.ParamName.Should().Be("Message");
        exception.Message.Should().Contain("Turn message cannot be empty.");
    }

    [Fact]
    public void ConstructorShouldTrimLeadingAndTrailingWhitespaceFromMessage()
    {
        BackendTurnRequest request = new(SampleContext, "   hello world   ");

        request.Message.Should().Be("hello world");
    }

    [Fact]
    public void ConstructorShouldPreserveContextReference()
    {
        BackendTurnRequest request = new(SampleContext, "hi");

        request.Context.Should().BeSameAs(SampleContext);
    }
}
