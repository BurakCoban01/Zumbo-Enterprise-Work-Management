namespace Zumbo.Api.Composition.Modules.WorkItems;

internal static class WorkItemBackgroundServiceComposition
{
    internal static IServiceCollection AddWorkItemBackgroundServices(
        this IServiceCollection services,
        IConfiguration? configuration)
    {
        if (configuration?.GetValue("BackgroundJobs:Enabled", true) == true)
        {
            services.AddHostedService<DueDateReminderHostedService>();
            services.AddHostedService<WorkItemRecurrenceSchedulerHostedService>();
            services.AddHostedService<WebhookDispatcherHostedService>();
            services.AddHostedService<DevelopmentWebhookReceiptRetentionHostedService>();
        }

        return services;
    }
}
