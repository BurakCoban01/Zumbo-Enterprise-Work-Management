using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Concurrency;
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
        services.AddScoped<GetNotificationPreferencesHandler>(provider =>
            new GetNotificationPreferencesHandler(
                provider.GetRequiredService<IDocumentRepository<NotificationPreferenceDocument>>(),
                provider.GetRequiredService<IDistributedLockProvider>(),
                provider.GetRequiredService<IOptions<DistributedLockOptions>>(),
                provider.GetRequiredService<ICurrentUser>()));
        services.AddScoped<UpdateNotificationPreferencesHandler>(provider =>
            new UpdateNotificationPreferencesHandler(
                provider.GetRequiredService<IDocumentRepository<NotificationPreferenceDocument>>(),
                provider.GetRequiredService<IDistributedLockProvider>(),
                provider.GetRequiredService<IOptions<DistributedLockOptions>>(),
                provider.GetRequiredService<IClock>(),
                provider.GetRequiredService<ICurrentUser>()));
        services.AddScoped<GetNotificationDeliveryMetricsHandler>(provider =>
            new GetNotificationDeliveryMetricsHandler(
                provider.GetRequiredService<IDocumentRepository<NotificationDocument>>(),
                provider.GetRequiredService<IClock>()));
        services.AddScoped<ListNotificationDeadLettersHandler>(provider =>
            new ListNotificationDeadLettersHandler(
                provider.GetRequiredService<IDocumentRepository<NotificationDocument>>()));
        services.AddScoped<ReplayNotificationDeadLetterHandler>(provider =>
            new ReplayNotificationDeadLetterHandler(
                provider.GetRequiredService<IDocumentRepository<NotificationDocument>>(),
                provider.GetRequiredService<IClock>()));
        services.AddScoped<CreateNotificationHandler>(provider =>
            new CreateNotificationHandler(
                provider.GetRequiredService<IDocumentRepository<NotificationDocument>>(),
                provider.GetRequiredService<IDocumentRepository<NotificationPreferenceDocument>>(),
                provider.GetRequiredService<INotificationUserDirectory>(),
                provider.GetRequiredService<IOptions<EmailNotificationOptions>>(),
                provider.GetRequiredService<IDistributedLockProvider>(),
                provider.GetRequiredService<IOptions<DistributedLockOptions>>(),
                provider.GetRequiredService<IClock>()));
        services.AddScoped<DispatchNotificationEmailsHandler>(provider =>
            new DispatchNotificationEmailsHandler(
                provider.GetRequiredService<IDocumentRepository<NotificationDocument>>(),
                provider.GetRequiredService<IEmailNotificationSender>(),
                provider.GetRequiredService<IOptions<EmailNotificationOptions>>(),
                provider.GetRequiredService<IClock>(),
                provider.GetService<IDurableMessageJitter>()));
        if (configuration.GetValue("BackgroundJobs:Enabled", true))
        {
            services.AddHostedService<NotificationEmailDispatcherHostedService>();
        }

        return services;
    }
}
