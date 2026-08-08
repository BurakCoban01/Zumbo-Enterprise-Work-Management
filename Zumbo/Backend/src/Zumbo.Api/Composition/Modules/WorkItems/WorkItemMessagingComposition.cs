using Zumbo.Api.Infrastructure.BackgroundServices.Webhooks;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.Modules.WorkItems;

namespace Zumbo.Api.Composition.Modules.WorkItems;

internal static class WorkItemMessagingComposition
{
    internal static IServiceCollection AddWorkItemPublicationServices(this IServiceCollection services)
    {
        services.AddScoped<SignalRWorkItemRealtimePublisher>();
        services.AddScoped<DurableWorkItemEventPublisher>();
        services.AddScoped<IWorkItemAuditPublisher>(provider => provider.GetRequiredService<DurableWorkItemEventPublisher>());
        services.AddScoped<IWorkItemNotificationPublisher>(provider => provider.GetRequiredService<DurableWorkItemEventPublisher>());
        services.AddScoped<IWorkItemSearchPublisher>(provider => provider.GetRequiredService<DurableWorkItemEventPublisher>());
        services.AddScoped<IWorkItemRealtimePublisher>(provider => provider.GetRequiredService<DurableWorkItemEventPublisher>());
        services.AddScoped<IWorkItemCacheInvalidationPublisher>(provider => provider.GetRequiredService<DurableWorkItemEventPublisher>());
        services.AddScoped<IWorkItemRecurrenceEventPublisher>(provider => provider.GetRequiredService<DurableWorkItemEventPublisher>());
        services.AddScoped<IWorkItemBulkJobEventPublisher>(provider => provider.GetRequiredService<DurableWorkItemEventPublisher>());
        services.AddScoped<IWorkItemAutomationEventPublisher>(provider => provider.GetRequiredService<DurableWorkItemEventPublisher>());
        services.AddScoped<IDevelopmentWebhookQueue>(provider => provider.GetRequiredService<DurableWorkItemEventPublisher>());
        services.AddScoped<IWorkItemAutomationChainContextAccessor, WorkItemAutomationChainContextAccessor>();
        return services;
    }

    internal static IServiceCollection AddWorkItemDurableEventHandlers(this IServiceCollection services)
    {
        services.AddScoped<IDurableEventHandler, WorkItemAuditDurableHandler>();
        services.AddScoped<IDurableEventHandler, WorkItemNotificationDurableHandler>();
        services.AddScoped<IDurableEventHandler, WorkItemSearchUpsertDurableHandler>();
        services.AddScoped<IDurableEventHandler, WorkItemSearchDeleteDurableHandler>();
        services.AddScoped<IDurableEventHandler, WorkItemRealtimeDurableHandler>();
        services.AddScoped<IDurableEventHandler, WorkItemCacheInvalidationDurableHandler>();
        services.AddScoped<IDurableEventHandler, WorkItemWebhookDurableHandler>();
        services.AddScoped<IDurableEventHandler, WorkItemRecurrenceDurableHandler>();
        services.AddScoped<IDurableEventHandler, WorkItemBulkJobDurableHandler>();
        services.AddScoped<IDurableEventHandler, DevelopmentWebhookProcessingDurableHandler>();
        return services;
    }
}
