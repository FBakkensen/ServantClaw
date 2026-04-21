using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ServantClaw.Application.Commands;
using ServantClaw.Application.Runtime;
using ServantClaw.Telegram.Transport;

namespace ServantClaw.Telegram;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddTelegramServices(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<IChatReplySink, TelegramChatReplySink>();
        services.TryAddSingleton<ITelegramPollingClientFactory, TelegramBotPollingClientFactory>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostRuntimeParticipant, TelegramPollingParticipant>());
        return services;
    }
}
