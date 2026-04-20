using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ServantClaw.Host;

public sealed partial class Worker(ILogger<Worker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        Log.HostStarted(logger);

        try
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            Log.HostStopping(logger);
        }
    }

    private static partial class Log
    {
        [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "ServantClaw host started")]
        public static partial void HostStarted(ILogger logger);

        [LoggerMessage(EventId = 2, Level = LogLevel.Information, Message = "ServantClaw host stopping")]
        public static partial void HostStopping(ILogger logger);
    }
}
