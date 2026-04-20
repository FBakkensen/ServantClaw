using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.WindowsServices;
using ServantClaw.Host.Configuration;
using ServantClaw.Host.Runtime;

namespace ServantClaw.Host;

public static class ServantClawHostBuilderExtensions
{
    private const string WindowsServiceName = "ServantClaw";

    public static HostApplicationBuilder AddServantClawHost(this HostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.Configure<WindowsServiceLifetimeOptions>(options => options.ServiceName = WindowsServiceName);
        builder.Services.AddWindowsService(options => options.ServiceName = WindowsServiceName);
        builder.Services.AddServantClawStartupConfiguration(builder.Configuration);
        builder.Services.AddSingleton<HostRuntimeCoordinator>();
        builder.Services.AddHostedService<Worker>();

        return builder;
    }
}
