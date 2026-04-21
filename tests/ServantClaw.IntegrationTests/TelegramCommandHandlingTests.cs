using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ServantClaw.Domain.Agents;
using ServantClaw.Domain.Common;
using ServantClaw.Domain.Routing;
using ServantClaw.Domain.State;
using ServantClaw.Host;
using ServantClaw.IntegrationTests.Testing;
using ServantClaw.Telegram.Transport;
using Xunit;

namespace ServantClaw.IntegrationTests;

public sealed class TelegramCommandHandlingTests
{
    [Fact]
    public async Task AgentCommandShouldPersistChatStateAndSendConfirmation()
    {
        FakeTelegramPollingClient pollingClient = new();
        await using TestHostContext context = await CreateStartedHost(pollingClient);

        pollingClient.EnqueueBatch(
            new TelegramIncomingUpdate(
                1,
                new TelegramIncomingMessage(100, 42, "approved-owner", DateTimeOffset.UtcNow, "/agent coding")));

        SentTelegramMessage reply = await pollingClient.DequeueSentMessageAsync(TimeSpan.FromSeconds(2));
        IStateStore stateStore = context.Host.Services.GetRequiredService<IStateStore>();
        ChatState? state = await stateStore.GetChatStateAsync(new ChatId(100), CancellationToken.None);

        reply.ChatId.Should().Be(100);
        reply.Text.Should().Be("Active agent set to 'coding'.");
        state.Should().NotBeNull();
        state!.ActiveAgent.Should().Be(AgentKind.Coding);
    }

    [Fact]
    public async Task ProjectCommandShouldRejectUnknownProjectAndLeaveExistingState()
    {
        FakeTelegramPollingClient pollingClient = new();
        await using TestHostContext context = await CreateStartedHost(pollingClient);
        IStateStore stateStore = context.Host.Services.GetRequiredService<IStateStore>();
        ChatState existingState = new(
            new ChatId(100),
            AgentKind.General,
            new AgentProjectBindings(new ProjectId("docs"), null));
        await stateStore.SaveChatStateAsync(existingState, CancellationToken.None);

        pollingClient.EnqueueBatch(
            new TelegramIncomingUpdate(
                1,
                new TelegramIncomingMessage(100, 42, "approved-owner", DateTimeOffset.UtcNow, "/project coding missing")));

        SentTelegramMessage reply = await pollingClient.DequeueSentMessageAsync(TimeSpan.FromSeconds(2));
        ChatState? currentState = await stateStore.GetChatStateAsync(new ChatId(100), CancellationToken.None);

        reply.Text.Should().Be("Unknown project 'missing'.");
        currentState.Should().Be(existingState);
    }

    [Fact]
    public async Task TextMessageShouldRefuseExecutionWhenNoActiveProjectIsBound()
    {
        FakeTelegramPollingClient pollingClient = new();
        await using TestHostContext context = await CreateStartedHost(pollingClient);
        IStateStore stateStore = context.Host.Services.GetRequiredService<IStateStore>();
        await stateStore.SaveChatStateAsync(
            new ChatState(new ChatId(100), AgentKind.Coding, new AgentProjectBindings()),
            CancellationToken.None);

        pollingClient.EnqueueBatch(
            new TelegramIncomingUpdate(
                1,
                new TelegramIncomingMessage(100, 42, "approved-owner", DateTimeOffset.UtcNow, "hello")));

        SentTelegramMessage reply = await pollingClient.DequeueSentMessageAsync(TimeSpan.FromSeconds(2));

        reply.Text.Should().Be(
            "No active project is selected for agent 'coding'. Use /project <agent-id> <project-id> before sending normal messages. Available projects: docs, repo.");
    }

    private static async ValueTask<TestHostContext> CreateStartedHost(FakeTelegramPollingClient pollingClient)
    {
        TestHostContext context = CreateHost(pollingClient);
        await context.Host.StartAsync();
        return context;
    }

    private static TestHostContext CreateHost(FakeTelegramPollingClient pollingClient)
    {
        string botRootPath = CreateTemporaryDirectory();
        Directory.CreateDirectory(Path.Combine(botRootPath, "projects", "docs"));
        Directory.CreateDirectory(Path.Combine(botRootPath, "projects", "repo"));

        HostApplicationBuilder builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            EnvironmentName = Environments.Development
        });

        builder.Configuration.Sources.Clear();
        builder.Configuration.AddInMemoryCollection(CreateValidConfiguration(botRootPath));
        builder.AddServantClawHost();
        builder.Services.AddSingleton<ITelegramPollingClientFactory>(new FakeTelegramPollingClientFactory(pollingClient));

        return new TestHostContext(builder.Build(), botRootPath);
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

    private sealed class TestHostContext : IAsyncDisposable
    {
        public TestHostContext(IHost host, string botRootPath)
        {
            Host = host;
            BotRootPath = botRootPath;
        }

        public IHost Host { get; }

        public string BotRootPath { get; }

        public async ValueTask DisposeAsync()
        {
            await Host.StopAsync();
            Host.Dispose();

            if (Directory.Exists(BotRootPath))
            {
                await DeleteDirectoryWithRetryAsync(BotRootPath);
            }
        }

        private static async Task DeleteDirectoryWithRetryAsync(string path)
        {
            const int maxAttempts = 5;

            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                try
                {
                    Directory.Delete(path, recursive: true);
                    return;
                }
                catch (IOException) when (attempt < maxAttempts)
                {
                    await Task.Delay(100);
                }
                catch (UnauthorizedAccessException) when (attempt < maxAttempts)
                {
                    await Task.Delay(100);
                }
            }
        }
    }
}
