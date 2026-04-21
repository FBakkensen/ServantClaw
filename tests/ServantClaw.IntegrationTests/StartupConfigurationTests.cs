using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using ServantClaw.Domain.Configuration;
using ServantClaw.Host;
using ServantClaw.Host.Configuration;
using ServantClaw.IntegrationTests.Testing;
using Xunit;

namespace ServantClaw.IntegrationTests;

public sealed class StartupConfigurationTests
{
    [Fact]
    public async Task HostShouldStartWhenStartupConfigurationIsValid()
    {
        using IHost host = CreateHost(CreateValidConfiguration());

        await host.StartAsync();

        ServiceConfiguration service = host.Services.GetRequiredService<ServiceConfiguration>();
        TelegramConfiguration telegram = host.Services.GetRequiredService<TelegramConfiguration>();
        OwnerConfiguration owner = host.Services.GetRequiredService<OwnerConfiguration>();

        service.BotRootPath.Should().Be("C:\\ServantClaw\\bot-root");
        service.Backend.Arguments.Should().ContainSingle().Which.Should().Be("app-server");
        telegram.BotToken.Should().Be("123456:ABCDEFGHIJKLMNOPQRSTUVWXYZabcd");
        owner.UserId.Value.Should().Be(42);

        await host.StopAsync();
    }

    [Fact]
    public async Task HostShouldFailStartupWhenTelegramTokenUsesPlaceholder()
    {
        Dictionary<string, string?> configuration = CreateValidConfiguration();
        configuration["Telegram:BotToken"] = TelegramOptions.BotTokenPlaceholder;

        using IHost host = CreateHost(configuration);

        Func<Task> act = () => host.StartAsync();

        OptionsValidationException exception = (await act.Should().ThrowAsync<OptionsValidationException>()).Which;
        exception.Failures.Should().Contain(failure => failure.Contains("Telegram:BotToken still uses the placeholder value.", StringComparison.Ordinal));
    }

    [Fact]
    public async Task HostShouldFailStartupWhenOwnerUserIdIsMissing()
    {
        Dictionary<string, string?> configuration = CreateValidConfiguration();
        configuration["Owner:UserId"] = "0";

        using IHost host = CreateHost(configuration);

        Func<Task> act = () => host.StartAsync();

        OptionsValidationException exception = (await act.Should().ThrowAsync<OptionsValidationException>()).Which;
        exception.Failures.Should().ContainSingle(failure => failure.Contains("Owner:UserId must be configured", StringComparison.Ordinal));
    }

    [Fact]
    public async Task HostShouldFailStartupWhenBackendExecutablePathIsMissing()
    {
        Dictionary<string, string?> configuration = CreateValidConfiguration();
        configuration["Service:Backend:ExecutablePath"] = "";

        using IHost host = CreateHost(configuration);

        Func<Task> act = () => host.StartAsync();

        OptionsValidationException exception = (await act.Should().ThrowAsync<OptionsValidationException>()).Which;
        exception.Failures.Should().Contain(failure => failure.Contains("Service:Backend:ExecutablePath must be configured.", StringComparison.Ordinal));
    }

    private static IHost CreateHost(IReadOnlyDictionary<string, string?> configurationValues)
    {
        HostApplicationBuilder builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            EnvironmentName = Environments.Development
        });

        builder.Configuration.Sources.Clear();
        builder.Configuration.AddInMemoryCollection(configurationValues);
        builder.AddServantClawHost();
        builder.Services.AddSingleton<ServantClaw.Telegram.Transport.ITelegramPollingClientFactory>(
            new FakeTelegramPollingClientFactory());

        return builder.Build();
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
}
