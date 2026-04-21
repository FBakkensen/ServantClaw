using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ServantClaw.Application.Runtime;
using ServantClaw.Codex.Transport;
using ServantClaw.Domain.Runtime;

namespace ServantClaw.Codex;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddCodexServices(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<IBackendProcessLauncher, SystemBackendProcessLauncher>();
        services.TryAddSingleton<IBackendRestartDelay, SystemBackendRestartDelay>();
        services.TryAddSingleton<BackendSessionCoordinator>();
        services.TryAddSingleton<IBackendSessionPublisher>(provider =>
            provider.GetRequiredService<BackendSessionCoordinator>());
        services.TryAddSingleton<IBackendSessionSource>(provider =>
            provider.GetRequiredService<BackendSessionCoordinator>());
        services.AddSingleton<BackendProcessSupervisor>();
        services.AddSingleton<IProcessSupervisor>(provider =>
            provider.GetRequiredService<BackendProcessSupervisor>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IHostRuntimeParticipant, BackendProcessSupervisor>(
                provider => provider.GetRequiredService<BackendProcessSupervisor>()));
        services.TryAddSingleton<IBackendClient, StdioCodexBackendClient>();

        return services;
    }
}
