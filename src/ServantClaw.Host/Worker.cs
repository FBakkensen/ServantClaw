using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ServantClaw.Host.Runtime;

namespace ServantClaw.Host;

public sealed partial class Worker(ILogger<Worker> logger, HostRuntimeCoordinator runtimeCoordinator) : IHostedService
{
    private bool hasStarted;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        Log.HostStarting(logger);

        try
        {
            await runtimeCoordinator.StartAsync(cancellationToken);
            hasStarted = true;
        }
        catch (Exception exception)
        {
            Log.HostStartupFailed(logger, exception);
            throw;
        }

        Log.HostStarted(logger);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (!hasStarted)
        {
            return;
        }

        Log.HostStopping(logger);
        await runtimeCoordinator.StopAsync(cancellationToken);
        Log.HostStopped(logger);
    }

    private static partial class Log
    {
        [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "ServantClaw host starting")]
        public static partial void HostStarting(ILogger logger);

        [LoggerMessage(EventId = 2, Level = LogLevel.Information, Message = "ServantClaw host started")]
        public static partial void HostStarted(ILogger logger);

        [LoggerMessage(EventId = 3, Level = LogLevel.Critical, Message = "ServantClaw host startup failed")]
        public static partial void HostStartupFailed(ILogger logger, Exception exception);

        [LoggerMessage(EventId = 4, Level = LogLevel.Information, Message = "ServantClaw host stopping")]
        public static partial void HostStopping(ILogger logger);

        [LoggerMessage(EventId = 5, Level = LogLevel.Information, Message = "ServantClaw host stopped")]
        public static partial void HostStopped(ILogger logger);
    }
}
