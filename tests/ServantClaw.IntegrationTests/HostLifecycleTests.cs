using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.WindowsServices;
using ServantClaw.Application.Runtime;
using ServantClaw.Host;
using ServantClaw.IntegrationTests.Testing;
using Xunit;

namespace ServantClaw.IntegrationTests;

public sealed class HostLifecycleTests
{
    [Fact]
    public async Task HostShouldStartRegisteredRuntimeParticipants()
    {
        RecordingParticipant participant = new();
        using IHost host = CreateHost(participant);

        await host.StartAsync();

        participant.StartCalls.Should().Be(1);
        participant.StopCalls.Should().Be(0);

        await host.StopAsync();
    }

    [Fact]
    public async Task HostShouldStopRegisteredRuntimeParticipantsOnShutdown()
    {
        RecordingParticipant participant = new();
        using IHost host = CreateHost(participant);

        await host.StartAsync();
        await host.StopAsync();

        participant.StartCalls.Should().Be(1);
        participant.StopCalls.Should().Be(1);
    }

    [Fact]
    public async Task HostShouldRollbackStartedParticipantsWhenStartupFails()
    {
        RecordingParticipant startedParticipant = new();
        ThrowingParticipant failingParticipant = new();
        using IHost host = CreateHost(startedParticipant, failingParticipant);

        Func<Task> act = () => host.StartAsync();

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("startup failed");
        startedParticipant.StartCalls.Should().Be(1);
        startedParticipant.StopCalls.Should().Be(1);
    }

    [Fact]
    public void HostShouldConfigureWindowsServiceIntegration()
    {
        HostApplicationBuilder builder = CreateHostBuilder();

        builder.Services.Should().Contain(descriptor =>
            descriptor.ServiceType == typeof(Microsoft.Extensions.Options.IConfigureOptions<WindowsServiceLifetimeOptions>));
    }

    private static IHost CreateHost(params IHostRuntimeParticipant[] participants)
    {
        HostApplicationBuilder builder = CreateHostBuilder();

        foreach (IHostRuntimeParticipant participant in participants)
        {
            builder.Services.AddSingleton<IHostRuntimeParticipant>(participant);
        }

        return builder.Build();
    }

    private static HostApplicationBuilder CreateHostBuilder()
    {
        HostApplicationBuilder builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            EnvironmentName = Environments.Development
        });

        builder.Configuration.Sources.Clear();
        builder.Configuration.AddInMemoryCollection(CreateValidConfiguration());
        builder.AddServantClawHost();
        builder.Services.AddSingleton<ServantClaw.Telegram.Transport.ITelegramPollingClientFactory>(
            new FakeTelegramPollingClientFactory());
        return builder;
    }

    private static Dictionary<string, string?> CreateValidConfiguration() =>
        new()
        {
            ["Service:BotRootPath"] = "C:\\ServantClaw\\bot-root",
            ["Service:ProjectsRootPath"] = "C:\\ServantClaw\\projects",
            ["Service:Backend:ExecutablePath"] = "C:\\tools\\codex.exe",
            ["Service:Backend:WorkingDirectory"] = "C:\\ServantClaw",
            ["Service:Backend:Arguments:0"] = "app-server",
            ["Telegram:BotToken"] = "123456:ABCDEFGHIJKLMNOPQRSTUVWXYZabcd",
            ["Telegram:Polling:Timeout"] = "00:00:30",
            ["Telegram:Polling:RetryDelay"] = "00:00:05",
            ["Owner:UserId"] = "42",
            ["Owner:Username"] = "approved-owner"
        };

    private sealed class RecordingParticipant : IHostRuntimeParticipant
    {
        public int StartCalls { get; private set; }

        public int StopCalls { get; private set; }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            StartCalls++;
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            StopCalls++;
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingParticipant : IHostRuntimeParticipant
    {
        public Task StartAsync(CancellationToken cancellationToken) =>
            throw new InvalidOperationException("startup failed");

        public Task StopAsync(CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }
}
