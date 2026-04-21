using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ServantClaw.Application.Commands;
using ServantClaw.Application.Intake;
using ServantClaw.Application.Runtime;
using ServantClaw.Infrastructure.Commands;
using ServantClaw.Domain.Runtime;
using ServantClaw.Domain.State;
using ServantClaw.Infrastructure.Intake;
using ServantClaw.Infrastructure.Runtime;
using ServantClaw.Infrastructure.State;

namespace ServantClaw.Infrastructure;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddInfrastructureServices(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<ChatCommandProcessor>();
        services.AddSingleton<ThreadMappingCoordinator>();
        services.AddSingleton<IProjectCatalog, FileSystemProjectCatalog>();
        services.AddSingleton<IThreadReferenceGenerator, GuidThreadReferenceGenerator>();
        services.AddSingleton<IStateStore, FileStateStore>();
        services.AddSingleton<IChatUpdateIntake, LoggingChatUpdateIntake>();
        services.TryAddSingleton<ITurnExecutor, NoOpTurnExecutor>();
        services.AddSingleton<PerContextTurnQueue>();
        services.AddSingleton<IPerContextTurnQueue>(provider => provider.GetRequiredService<PerContextTurnQueue>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IHostRuntimeParticipant, PerContextTurnQueue>(
                provider => provider.GetRequiredService<PerContextTurnQueue>()));
        return services;
    }
}
