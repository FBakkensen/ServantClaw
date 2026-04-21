using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using ServantClaw.Application.Runtime;
using ServantClaw.Domain.Configuration;

namespace ServantClaw.Codex;

[ExcludeFromCodeCoverage]
public sealed class SystemBackendProcessLauncher : IBackendProcessLauncher
{
    public IBackendProcessHandle Launch(BackendConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        ProcessStartInfo startInfo = new()
        {
            FileName = configuration.ExecutablePath,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = configuration.WorkingDirectory ?? string.Empty,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        foreach (string argument in configuration.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        Process process = new() { StartInfo = startInfo, EnableRaisingEvents = true };

        if (!process.Start())
        {
            process.Dispose();
            throw new InvalidOperationException(
                $"Failed to start backend process '{configuration.ExecutablePath}'.");
        }

        return new SystemBackendProcessHandle(process);
    }
}
