using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ServantClaw.Host.Configuration;
using ServantClaw.Host;

var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory
});

builder.Services.AddServantClawStartupConfiguration(builder.Configuration);
builder.Services.AddHostedService<Worker>();

var host = builder.Build();
await host.RunAsync();
