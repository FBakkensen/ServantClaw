using Microsoft.Extensions.Hosting;
using ServantClaw.Host;
using ServantClaw.Host.Logging;
using Serilog;

var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory
});

Log.Logger = ServantClawSerilogConfiguration.CreateBootstrapLogger(builder.Configuration);

try
{
    builder.AddServantClawHost();

    var host = builder.Build();
    await host.RunAsync();
}
catch (Exception exception)
{
    Log.Fatal(exception, "ServantClaw host terminated unexpectedly");
    throw;
}
finally
{
    await Log.CloseAndFlushAsync();
}
