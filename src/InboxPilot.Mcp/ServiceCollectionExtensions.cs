using InboxPilot.Mcp.Auditing;
using InboxPilot.Mcp.Configuration;
using InboxPilot.Mcp.Mail;
using InboxPilot.Mcp.Notifications;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace InboxPilot.Mcp;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddInboxPilot(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<InboxPilotOptions>(configuration.GetSection(InboxPilotOptions.SectionName));
        services.AddSingleton(TimeProvider.System);

        services.AddSingleton<IMailProvider, GmailProvider>();
        services.AddSingleton<IMailProvider, OutlookProvider>();
        services.AddSingleton<MailProviderRegistry>();

        services.AddHttpClient<TelegramNotifier>(c => c.Timeout = TimeSpan.FromSeconds(20));
        services.AddHttpClient<WhatsAppNotifier>(c => c.Timeout = TimeSpan.FromSeconds(20));
        services.AddTransient<IOwnerNotifier>(sp =>
        {
            var channel = sp.GetRequiredService<IOptions<InboxPilotOptions>>().Value.Notify.Channel;
            return channel.Equals("whatsapp", StringComparison.OrdinalIgnoreCase)
                ? sp.GetRequiredService<WhatsAppNotifier>()
                : sp.GetRequiredService<TelegramNotifier>();
        });

        services.AddSingleton<NotificationGuard>();
        services.AddSingleton<AuditLog>();
        return services;
    }
}
