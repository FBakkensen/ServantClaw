using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.WindowsServices;
using ServantClaw.Host.Configuration;
using ServantClaw.Host.Logging;
using ServantClaw.Host.Runtime;
using ServantClaw.Infrastructure;
using Serilog;
using ServantClaw.Telegram;

namespace ServantClaw.Host;

public static class ServantClawHostBuilderExtensions
{
    private const string WindowsServiceName = "ServantClaw";

    public static HostApplicationBuilder AddServantClawHost(this HostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddSerilog(
            configureLogger: (_, loggerConfiguration) =>
                ServantClawSerilogConfiguration.Configure(loggerConfiguration, builder.Configuration),
            preserveStaticLogger: false,
            writeToProviders: false);
        builder.Services.Configure<WindowsServiceLifetimeOptions>(options => options.ServiceName = WindowsServiceName);
        builder.Services.AddWindowsService(options => options.ServiceName = WindowsServiceName);
        builder.Services.AddServantClawStartupConfiguration(builder.Configuration);
        builder.Services.AddInfrastructureServices();
        builder.Services.AddTelegramServices();
        builder.Services.AddSingleton<HostRuntimeCoordinator>();
        builder.Services.AddHostedService<Worker>();

        return builder;
    }
}
