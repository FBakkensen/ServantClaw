using System.Globalization;
using FluentAssertions;
using ServantClaw.Domain.Agents;
using ServantClaw.Domain.Approvals;
using ServantClaw.Domain.Common;
using ServantClaw.Domain.Configuration;
using ServantClaw.Domain.Routing;
using ServantClaw.Domain.Runtime;
using ServantClaw.Domain.State;
using Xunit;

namespace ServantClaw.UnitTests;

public sealed class DomainContractsTests
{
    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void StronglyTypedStringIdentifiersShouldRejectEmptyValues(string value)
    {
        Action createProjectId = () => _ = new ProjectId(value);
        Action createThreadReference = () => _ = new ThreadReference(value);
        Action createApprovalId = () => _ = new ApprovalId(value);

        createProjectId.Should().Throw<ArgumentException>();
        createThreadReference.Should().Throw<ArgumentException>();
        createApprovalId.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ChatStateShouldRejectNullProjectBindings()
    {
        Action act = () => _ = new ChatState(new ChatId(42), AgentKind.General, null!);

        act.Should().Throw<ArgumentNullException>().Which.ParamName.Should().Be("projectBindings");
    }

    [Fact]
    public void ChatStateShouldTrackActiveAgentAndPerAgentProjectBindings()
    {
        ChatState state = new(
            new ChatId(42),
            AgentKind.General,
            new AgentProjectBindings());

        ChatState updated = state
            .BindProject(AgentKind.General, new ProjectId("docs"))
            .BindProject(AgentKind.Coding, new ProjectId("repo"))
            .SetActiveAgent(AgentKind.Coding);

        updated.ProjectBindings.GeneralProjectId.Should().Be(new ProjectId("docs"));
        updated.ProjectBindings.CodingProjectId.Should().Be(new ProjectId("repo"));
        updated.GetActiveProject().Should().Be(new ProjectId("repo"));
    }

    [Fact]
    public void ThreadMappingRotateShouldPromoteNewCurrentThreadAndPreserveHistory()
    {
        ThreadMapping mapping = new(
            new ThreadContext(new ChatId(42), AgentKind.Coding, new ProjectId("repo")),
            new ThreadReference("thread-1"));

        ThreadMapping rotated = mapping.Rotate(new ThreadReference("thread-2"));

        rotated.CurrentThread.Should().Be(new ThreadReference("thread-2"));
        rotated.PreviousThreads.Should().ContainSingle().Which.Should().Be(new ThreadReference("thread-1"));
    }

    [Fact]
    public void ApprovalRecordShouldResolveOnceAndCaptureDecision()
    {
        DateTimeOffset createdAt = DateTimeOffset.Parse("2026-04-20T08:00:00+00:00", CultureInfo.InvariantCulture);
        DateTimeOffset resolvedAt = DateTimeOffset.Parse("2026-04-20T08:05:00+00:00", CultureInfo.InvariantCulture);

        ApprovalRecord pending = new(
            new ApprovalId("approval-1"),
            ApprovalClass.MaintenanceAction,
            new ApprovalContext(
                new ChatId(42),
                AgentKind.Coding,
                new ProjectId("repo"),
                new ThreadReference("thread-1")),
            "Upgrade the backend runtime",
            createdAt);

        ApprovalRecord resolved = pending.Resolve(ApprovalDecision.Approved, resolvedAt);

        resolved.IsPending.Should().BeFalse();
        resolved.Decision.Should().Be(ApprovalDecision.Approved);
        resolved.ResolvedAt.Should().Be(resolvedAt);
    }

    [Fact]
    public void ApprovalRecordShouldRejectSecondResolution()
    {
        DateTimeOffset createdAt = DateTimeOffset.Parse("2026-04-20T08:00:00+00:00", CultureInfo.InvariantCulture);
        DateTimeOffset deniedAt = DateTimeOffset.Parse("2026-04-20T08:01:00+00:00", CultureInfo.InvariantCulture);
        DateTimeOffset approvedAt = DateTimeOffset.Parse("2026-04-20T08:02:00+00:00", CultureInfo.InvariantCulture);

        ApprovalRecord pending = new(
            new ApprovalId("approval-1"),
            ApprovalClass.StandardRiskyAction,
            new ApprovalContext(
                new ChatId(42),
                AgentKind.General,
                new ProjectId("docs"),
                new ThreadReference("thread-1")),
            "Open a browser tab",
            createdAt);

        ApprovalRecord resolved = pending.Resolve(ApprovalDecision.Denied, deniedAt);

        Action act = () => resolved.Resolve(ApprovalDecision.Approved, approvedAt);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void ConfigurationContractsShouldKeepStartupSettingsTransportNeutral()
    {
        ServiceConfiguration service = new(
            "C:\\ServantClaw",
            "C:\\ServantClaw\\projects",
            new BackendConfiguration("codex", "C:\\ServantClaw", ["app-server"]));

        TelegramConfiguration telegram = new(
            "token",
            new PollingConfiguration(TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(5)));

        service.Backend.Arguments.Should().ContainSingle().Which.Should().Be("app-server");
        telegram.Polling.Timeout.Should().Be(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void DomainBoundaryInterfacesShouldRemainAvailableFromDomain()
    {
        Type[] interfaceTypes =
        [
            typeof(IStateStore),
            typeof(IBackendClient),
            typeof(IThreadReferenceGenerator),
            typeof(IClock),
            typeof(IIdGenerator),
            typeof(IProcessSupervisor)
        ];

        interfaceTypes.Should().OnlyContain(type => type.IsInterface);
    }
}
