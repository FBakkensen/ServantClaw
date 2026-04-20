using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using ServantClaw.Domain.Configuration;

namespace ServantClaw.Host.Configuration;

public static class ServantClawStartupConfigurationExtensions
{
    public static IServiceCollection AddServantClawStartupConfiguration(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services
            .AddOptionsWithValidateOnStart<ServiceOptions>()
            .Bind(configuration.GetSection(ServiceOptions.SectionName))
            .ValidateDataAnnotations();

        services
            .AddOptionsWithValidateOnStart<TelegramOptions>()
            .Bind(configuration.GetSection(TelegramOptions.SectionName))
            .ValidateDataAnnotations();

        services
            .AddOptionsWithValidateOnStart<OwnerOptions>()
            .Bind(configuration.GetSection(OwnerOptions.SectionName));

        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<ServiceOptions>, ValidateServiceOptions>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<TelegramOptions>, ValidateTelegramOptions>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<OwnerOptions>, ValidateOwnerOptions>());

        services.AddSingleton(serviceProvider =>
            serviceProvider.GetRequiredService<IOptions<ServiceOptions>>().Value.ToDomainConfiguration());
        services.AddSingleton(serviceProvider =>
            serviceProvider.GetRequiredService<IOptions<TelegramOptions>>().Value.ToDomainConfiguration());
        services.AddSingleton(serviceProvider =>
            serviceProvider.GetRequiredService<IOptions<OwnerOptions>>().Value.ToDomainConfiguration());

        return services;
    }
}
