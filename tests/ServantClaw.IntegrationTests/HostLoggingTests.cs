using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using ServantClaw.Host;
using ServantClaw.Host.Configuration;
using ServantClaw.Host.Logging;
using ServantClaw.Host.Runtime;
using Serilog;
using Xunit;

namespace ServantClaw.IntegrationTests;

public sealed class HostLoggingTests
{
    [Fact]
    public async Task HostShouldWriteDurableLogsForSuccessfulLifecycle()
    {
        string botRootPath = CreateTemporaryDirectory();
        IHost host = CreateHost(CreateValidConfiguration(botRootPath));

        try
        {
            await host.StartAsync();
            await host.StopAsync();
        }
        finally
        {
            host.Dispose();
        }

        string logContents = await ReadLogContentsAsync(botRootPath);

        logContents.Should().Contain("ServantClaw host starting");
        logContents.Should().Contain("ServantClaw host started");
        logContents.Should().Contain("ServantClaw host stopping");
        logContents.Should().Contain("ServantClaw host stopped");
    }

    [Fact]
    public async Task HostShouldWriteDurableLogsWhenRuntimeParticipantStartupFails()
    {
        string botRootPath = CreateTemporaryDirectory();
        IHost host = CreateHost(CreateValidConfiguration(botRootPath), new ThrowingParticipant());

        try
        {
            Func<Task> act = () => host.StartAsync();

            await act.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("startup failed");
        }
        finally
        {
            host.Dispose();
        }

        string logContents = await ReadLogContentsAsync(botRootPath);

        logContents.Should().Contain("Runtime participant ThrowingParticipant failed during startup");
        logContents.Should().Contain("ServantClaw host startup failed");
    }

    [Fact]
    public async Task HostShouldWriteDurableLogsWhenConfigurationValidationFails()
    {
        string botRootPath = CreateTemporaryDirectory();
        Dictionary<string, string?> configurationValues = CreateValidConfiguration(botRootPath);
        configurationValues["Telegram:BotToken"] = TelegramOptions.BotTokenPlaceholder;

        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configurationValues)
            .Build();

        Log.Logger = ServantClawSerilogConfiguration.CreateBootstrapLogger(configuration);

        try
        {
            using IHost host = CreateHost(configurationValues);

            Func<Task> act = () => host.StartAsync();

            OptionsValidationException exception = (await act.Should().ThrowAsync<OptionsValidationException>()).Which;
            Log.Fatal(exception, "ServantClaw host terminated unexpectedly");
        }
        finally
        {
            await Log.CloseAndFlushAsync();
        }

        string logContents = await ReadLogContentsAsync(botRootPath);

        logContents.Should().Contain("ServantClaw host terminated unexpectedly");
        logContents.Should().Contain("Telegram:BotToken still uses the placeholder value.");
    }

    private static IHost CreateHost(
        IReadOnlyDictionary<string, string?> configurationValues,
        params IHostRuntimeParticipant[] participants)
    {
        HostApplicationBuilder builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            EnvironmentName = Environments.Development
        });

        builder.Configuration.Sources.Clear();
        builder.Configuration.AddInMemoryCollection(configurationValues);
        builder.AddServantClawHost();

        foreach (IHostRuntimeParticipant participant in participants)
        {
            builder.Services.AddSingleton<IHostRuntimeParticipant>(participant);
        }

        return builder.Build();
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

    private static async Task<string> ReadLogContentsAsync(string botRootPath)
    {
        string logDirectoryPath = Path.Combine(botRootPath, "logs");
        return await WaitForLogContentsAsync(logDirectoryPath);
    }

    private static async Task<string> WaitForLogContentsAsync(string logDirectoryPath)
    {
        for (int attempt = 0; attempt < 50; attempt++)
        {
            if (Directory.Exists(logDirectoryPath))
            {
                string[] logFiles = Directory.GetFiles(logDirectoryPath, "*.log*");
                List<string> contents = [];

                foreach (string logFilePath in logFiles)
                {
                    string fileContents = await File.ReadAllTextAsync(logFilePath);

                    if (!string.IsNullOrWhiteSpace(fileContents))
                    {
                        contents.Add(fileContents);
                    }
                }

                if (contents.Count > 0)
                {
                    return string.Join(Environment.NewLine, contents);
                }
            }

            await Task.Delay(100);
        }

        return string.Empty;
    }

    private sealed class ThrowingParticipant : IHostRuntimeParticipant
    {
        public Task StartAsync(CancellationToken cancellationToken) =>
            throw new InvalidOperationException("startup failed");

        public Task StopAsync(CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }
}
