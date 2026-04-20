using Microsoft.Extensions.Hosting;
using ServantClaw.Host;

var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory
});

builder.AddServantClawHost();

var host = builder.Build();
await host.RunAsync();
