using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.Modules.Notifications;
using Zumbo.SharedKernel;

namespace Zumbo.Api.Composition.Modules.Notifications;

internal static class NotificationModuleComposition
{
    internal static IServiceCollection AddNotificationServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<EmailNotificationOptions>()
            .BindConfiguration("Notifications:Email")
            .Validate(options => !options.Enabled
                || (!string.IsNullOrWhiteSpace(options.Host)
                    && options.Port is >= 1 and <= 65535
                    && options.MaxAttempts is >= 1 and <= 20
                    && options.BaseRetrySeconds is >= 1 and <= 3600
                    && options.MaximumRetrySeconds >= options.BaseRetrySeconds
                    && options.MaximumRetrySeconds <= 86400
                    && options.RetryJitterRatio is >= 0 and <= 1
                    && options.LeaseSeconds is >= 5 and <= 900
                    && options.DispatchBatchSize is >= 1 and <= 100
                    && options.DispatcherIntervalSeconds is >= 1 and <= 3600),
                "Notification email delivery configuration is invalid.")
            .ValidateOnStart();
        services.AddScoped<INotificationUserDirectory, NotificationUserDirectoryAdapter>();
        services.AddScoped<INotificationAuditWriter, NotificationAuditWriterAdapter>();
        services.AddScoped<IEmailNotificationSender, SmtpEmailNotificationSender>();
        services.AddScoped<NotificationService>();
        services.AddScoped<ListNotificationsHandler>(provider => new ListNotificationsHandler(
            provider.GetRequiredService<IDocumentRepository<NotificationDocument>>(),
            provider.GetRequiredService<ICurrentUser>()));
        services.AddScoped<MarkNotificationAsReadHandler>(provider => new MarkNotificationAsReadHandler(
            provider.GetRequiredService<IDocumentRepository<NotificationDocument>>(),
            provider.GetRequiredService<ICurrentUser>()));
        if (configuration.GetValue("BackgroundJobs:Enabled", true))
        {
            services.AddHostedService<NotificationEmailDispatcherHostedService>();
        }

        return services;
    }
}
