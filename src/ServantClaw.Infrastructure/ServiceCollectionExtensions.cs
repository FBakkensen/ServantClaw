using Microsoft.Extensions.DependencyInjection;
using ServantClaw.Domain.State;
using ServantClaw.Infrastructure.State;

namespace ServantClaw.Infrastructure;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddInfrastructureServices(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IStateStore, FileStateStore>();
        return services;
    }
}
