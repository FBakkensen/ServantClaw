using System.Globalization;
using Microsoft.Extensions.Configuration;
using Serilog;
using Serilog.Configuration;
using Serilog.Core;
using Serilog.Events;

namespace ServantClaw.Host.Logging;

internal static class ServantClawSerilogConfiguration
{
    private const string DefaultLogFileName = "servantclaw-.log";
    private const string FallbackLogsDirectoryName = "logs";
    private const string ServiceSectionPath = "Service:BotRootPath";
    private const long FileSizeLimitBytes = 10 * 1024 * 1024;
    private const int RetainedFileCountLimit = 14;
    private static readonly TimeSpan FlushToDiskInterval = TimeSpan.FromSeconds(1);

    public static Logger CreateBootstrapLogger(IConfiguration configuration) =>
        Configure(new LoggerConfiguration(), configuration).CreateLogger();

    public static LoggerConfiguration Configure(LoggerConfiguration loggerConfiguration, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(loggerConfiguration);
        ArgumentNullException.ThrowIfNull(configuration);

        string logsDirectoryPath = ResolveLogsDirectoryPath(configuration);
        string logFilePath = Path.Combine(logsDirectoryPath, DefaultLogFileName);

        Directory.CreateDirectory(logsDirectoryPath);

        ApplyMinimumLevels(loggerConfiguration, configuration);

        return loggerConfiguration
            .Enrich.FromLogContext()
            .Enrich.WithProperty("Application", "ServantClaw")
            .WriteTo.File(
                path: logFilePath,
                rollingInterval: RollingInterval.Day,
                rollOnFileSizeLimit: true,
                retainedFileCountLimit: RetainedFileCountLimit,
                fileSizeLimitBytes: FileSizeLimitBytes,
                shared: true,
                flushToDiskInterval: FlushToDiskInterval,
                formatProvider: CultureInfo.InvariantCulture,
                outputTemplate:
                "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext} {Message:lj}{NewLine}{Exception}");
    }

    private static void ApplyMinimumLevels(LoggerConfiguration loggerConfiguration, IConfiguration configuration)
    {
        IConfigurationSection logLevelsSection = configuration.GetSection("Logging:LogLevel");
        LogEventLevel defaultLevel = TryParseLogEventLevel(logLevelsSection["Default"], out LogEventLevel configuredDefaultLevel)
            ? configuredDefaultLevel
            : LogEventLevel.Information;

        LoggerMinimumLevelConfiguration minimumLevelConfiguration = loggerConfiguration.MinimumLevel;
        minimumLevelConfiguration.Is(defaultLevel);

        foreach (IConfigurationSection overrideSection in logLevelsSection.GetChildren())
        {
            if (string.Equals(overrideSection.Key, "Default", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (TryParseLogEventLevel(overrideSection.Value, out LogEventLevel overrideLevel))
            {
                minimumLevelConfiguration.Override(overrideSection.Key, overrideLevel);
            }
        }
    }

    private static string ResolveLogsDirectoryPath(IConfiguration configuration)
    {
        string? botRootPath = configuration[ServiceSectionPath];

        if (!string.IsNullOrWhiteSpace(botRootPath))
        {
            return Path.Combine(botRootPath.Trim(), FallbackLogsDirectoryName);
        }

        return Path.Combine(AppContext.BaseDirectory, FallbackLogsDirectoryName);
    }

    private static bool TryParseLogEventLevel(string? value, out LogEventLevel logEventLevel) =>
        Enum.TryParse(value, ignoreCase: true, out logEventLevel);
}
