using System.Text.Json;
using System.Globalization;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ServantClaw.Domain.Agents;
using ServantClaw.Domain.Approvals;
using ServantClaw.Domain.Common;
using ServantClaw.Domain.Configuration;
using ServantClaw.Domain.Routing;
using ServantClaw.Domain.State;
using ServantClaw.Host;
using Xunit;

namespace ServantClaw.IntegrationTests;

public sealed class FileStateStoreTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task StateStoreShouldRoundTripChatState()
    {
        using TestHostContext context = CreateTestHost();
        IStateStore store = context.Services.GetRequiredService<IStateStore>();

        ChatState expected = new(
            new ChatId(42),
            AgentKind.Coding,
            new AgentProjectBindings(new ProjectId("general-project"), new ProjectId("coding-project")));

        await store.SaveChatStateAsync(expected, CancellationToken.None);

        ChatState? actual = await store.GetChatStateAsync(expected.ChatId, CancellationToken.None);

        actual.Should().Be(expected);

        string persistedPath = Path.Combine(context.BotRootPath, "state", "chats", "42.json");
        string persistedContents = await File.ReadAllTextAsync(persistedPath);

        persistedContents.Should().Contain("\"activeAgent\": \"Coding\"");
        persistedContents.Should().Contain("\"codingProjectId\": \"coding-project\"");
    }

    [Fact]
    public async Task StateStoreShouldRoundTripThreadMappingsWithHistory()
    {
        using TestHostContext context = CreateTestHost();
        IStateStore store = context.Services.GetRequiredService<IStateStore>();

        ThreadMapping expected = new(
            new ThreadContext(new ChatId(42), AgentKind.Coding, new ProjectId("repo")),
            new ThreadReference("thread-2"),
            [new ThreadReference("thread-1")]);

        await store.SaveThreadMappingAsync(expected, CancellationToken.None);

        ThreadMapping? actual = await store.GetThreadMappingAsync(expected.Context, CancellationToken.None);

        actual.Should().BeEquivalentTo(expected);
    }

    [Fact]
    public async Task StateStoreShouldRoundTripThreadMappingWithNullCurrentThread()
    {
        using TestHostContext context = CreateTestHost();
        IStateStore store = context.Services.GetRequiredService<IStateStore>();

        ThreadMapping expected = new(
            new ThreadContext(new ChatId(77), AgentKind.General, new ProjectId("docs")),
            null,
            [new ThreadReference("thread-historic")]);

        await store.SaveThreadMappingAsync(expected, CancellationToken.None);

        ThreadMapping? actual = await store.GetThreadMappingAsync(expected.Context, CancellationToken.None);

        actual.Should().NotBeNull();
        actual!.CurrentThread.Should().BeNull();
        actual.PreviousThreads.Should().ContainSingle().Which.Should().Be(new ThreadReference("thread-historic"));
    }

    [Fact]
    public async Task StateStoreShouldPersistAndQueryPendingApprovals()
    {
        using TestHostContext context = CreateTestHost();
        IStateStore store = context.Services.GetRequiredService<IStateStore>();

        ApprovalRecord pending = CreatePendingApproval("approval-1");
        ApprovalRecord resolved = pending.Resolve(ApprovalDecision.Approved, pending.CreatedAt.AddMinutes(2));

        await store.SaveApprovalAsync(pending, CancellationToken.None);
        await store.SaveApprovalAsync(resolved, CancellationToken.None);

        ApprovalRecord? approval = await store.GetApprovalAsync(resolved.ApprovalId, CancellationToken.None);
        IReadOnlyCollection<ApprovalRecord> pendingApprovals = await store.GetPendingApprovalsAsync(CancellationToken.None);

        approval.Should().BeEquivalentTo(resolved);
        pendingApprovals.Should().BeEmpty();

        string resolvedPath = Path.Combine(context.BotRootPath, "state", "approvals", "resolved", "approval-1.json");
        File.Exists(resolvedPath).Should().BeTrue();
        string pendingPath = Path.Combine(context.BotRootPath, "state", "approvals", "pending", "approval-1.json");
        File.Exists(pendingPath).Should().BeFalse();
    }

    [Fact]
    public async Task StateStoreShouldReadPersistedOwnerConfiguration()
    {
        using TestHostContext context = CreateTestHost();
        string configDirectory = Path.Combine(context.BotRootPath, "config");
        Directory.CreateDirectory(configDirectory);

        string ownerPath = Path.Combine(configDirectory, "owner.json");
        await File.WriteAllTextAsync(ownerPath, """
            {
              "userId": 99,
              "username": "repair-operator"
            }
            """);

        IStateStore store = context.Services.GetRequiredService<IStateStore>();

        OwnerConfiguration? owner = await store.GetOwnerConfigurationAsync(CancellationToken.None);

        owner.Should().Be(new OwnerConfiguration(new UserId(99), "repair-operator"));
    }

    [Fact]
    public async Task StateStoreShouldQuarantineCorruptedChatStateAndWriteRecoveryIncident()
    {
        using TestHostContext context = CreateTestHost();
        string chatStatePath = Path.Combine(context.BotRootPath, "state", "chats", "42.json");
        Directory.CreateDirectory(Path.GetDirectoryName(chatStatePath)!);
        await File.WriteAllTextAsync(chatStatePath, "{ not valid json");

        IStateStore store = context.Services.GetRequiredService<IStateStore>();

        ChatState? state = await store.GetChatStateAsync(new ChatId(42), CancellationToken.None);

        state.Should().BeNull();
        File.Exists(chatStatePath).Should().BeFalse();

        string quarantineDirectory = Path.Combine(context.BotRootPath, "state", "quarantine", "chats");
        string[] quarantinedFiles = Directory.GetFiles(quarantineDirectory, "42.*.corrupt");
        quarantinedFiles.Should().ContainSingle();
        (await File.ReadAllTextAsync(quarantinedFiles[0])).Should().Contain("{ not valid json");

        string incidentsDirectory = Path.Combine(context.BotRootPath, "state", "recovery", "incidents");
        string[] incidents = Directory.GetFiles(incidentsDirectory, "chat-state.*.json", SearchOption.AllDirectories);
        incidents.Should().ContainSingle();

        StateCorruptionIncidentDocument? incident = JsonSerializer.Deserialize<StateCorruptionIncidentDocument>(
            await File.ReadAllTextAsync(incidents[0]),
            JsonOptions);

        incident.Should().NotBeNull();
        incident!.RecordType.Should().Be("chat-state");
        incident.CanonicalPath.Should().Be(chatStatePath);
        incident.QuarantinePath.Should().Be(quarantinedFiles[0]);
        incident.Failure.Should().NotBeNullOrWhiteSpace();
    }

    private static ApprovalRecord CreatePendingApproval(string approvalId) =>
        new(
            new ApprovalId(approvalId),
            ApprovalClass.StandardRiskyAction,
            new ApprovalContext(
                new ChatId(42),
                AgentKind.Coding,
                new ProjectId("repo"),
                new ThreadReference("thread-1")),
            "Approve update",
            DateTimeOffset.Parse("2026-04-21T10:00:00+00:00", CultureInfo.InvariantCulture),
            new Dictionary<string, string>
            {
                ["operation"] = "update"
            });

    private static TestHostContext CreateTestHost()
    {
        string botRootPath = CreateTemporaryDirectory();
        HostApplicationBuilder builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            EnvironmentName = Environments.Development
        });

        builder.Configuration.Sources.Clear();
        builder.Configuration.AddInMemoryCollection(CreateValidConfiguration(botRootPath));
        builder.AddServantClawHost();

        IHost host = builder.Build();
        return new TestHostContext(host, botRootPath);
    }

    private static Dictionary<string, string?> CreateValidConfiguration(string botRootPath) =>
        new()
        {
            ["Service:BotRootPath"] = botRootPath,
            ["Service:ProjectsRootPath"] = Path.Combine(botRootPath, "projects"),
            ["Service:Backend:ExecutablePath"] = "C:\\tools\\codex.exe",
            ["Service:Backend:WorkingDirectory"] = "C:\\ServantClaw",
            ["Service:Backend:Arguments:0"] = "app-server",
            ["Telegram:BotToken"] = "123456:ABCDEFGHIJKLMNOPQRSTUVWXYZabcd",
            ["Telegram:Polling:Timeout"] = "00:00:30",
            ["Telegram:Polling:RetryDelay"] = "00:00:05",
            ["Owner:UserId"] = "42",
            ["Owner:Username"] = "approved-owner"
        };

    private static string CreateTemporaryDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), "ServantClawTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class TestHostContext : IDisposable
    {
        private readonly IHost host;

        public TestHostContext(IHost host, string botRootPath)
        {
            this.host = host;
            BotRootPath = botRootPath;
        }

        public IServiceProvider Services => host.Services;

        public string BotRootPath { get; }

        public void Dispose()
        {
            host.Dispose();

            if (Directory.Exists(BotRootPath))
            {
                Directory.Delete(BotRootPath, recursive: true);
            }
        }
    }

    private sealed record StateCorruptionIncidentDocument(
        string RecordType,
        string CanonicalPath,
        string QuarantinePath,
        string Failure,
        DateTimeOffset DetectedAtUtc);
}
