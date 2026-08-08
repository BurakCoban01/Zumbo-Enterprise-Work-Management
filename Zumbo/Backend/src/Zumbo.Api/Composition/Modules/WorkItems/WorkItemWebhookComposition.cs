using Zumbo.Modules.WorkItems;
using Zumbo.Modules.WorkItems.Application.Features.Webhooks.Deliveries;
using Zumbo.Modules.WorkItems.Application.Features.Webhooks.Subscriptions;

namespace Zumbo.Api.Composition.Modules.WorkItems;

internal static class WorkItemWebhookComposition
{
    internal static IServiceCollection AddWorkItemWebhookServices(this IServiceCollection services)
    {
        services.AddOptions<WebhookOptions>()
            .BindConfiguration("Webhooks")
            .Validate(options => options.MaximumAttempts is >= 1 and <= 20
                && options.BaseRetrySeconds is >= 1 and <= 3600
                && options.MaximumRetrySeconds >= options.BaseRetrySeconds
                && options.MaximumRetrySeconds <= 86400
                && options.RetryJitterRatio is >= 0 and <= 1
                && options.LeaseSeconds is >= 5 and <= 900
                && options.RequestTimeoutSeconds is >= 1 and <= 30
                && options.DispatchBatchSize is >= 1 and <= 100
                && options.DispatcherIntervalSeconds is >= 1 and <= 3600
                && options.RotationOverlapMinutes is >= 1 and <= 1440,
                "Webhook delivery configuration is invalid.")
            .ValidateOnStart();
        services.AddSingleton<WebhookTargetPolicy>();
        services.AddSingleton<IWebhookTargetPolicy>(provider => provider.GetRequiredService<WebhookTargetPolicy>());
        services.AddSingleton<IWebhookSecretProtector, WebhookSecretProtectorAdapter>();
        services.AddSingleton<IWebhookSender, PinnedWebhookSender>();
        services.AddScoped<IWebhookAuthorization, WebhookAuthorizationAdapter>();
        services.AddScoped<ListWebhookSubscriptionsHandler>();
        services.AddScoped<GetWebhookSubscriptionHandler>();
        services.AddScoped<GetWebhookDeliveryMetricsHandler>();
        services.AddScoped<ListWebhookDeliveriesHandler>();
        services.AddScoped<GetWebhookDeliveryHandler>();
        services.AddScoped<ReplayWebhookDeliveryHandler>();
        services.AddScoped<SetSubscriptionStateHandler>();
        services.AddScoped<UpdateSubscriptionHandler>();
        services.AddScoped<CreateSubscriptionHandler>();
        services.AddScoped<RotateSecretHandler>();
        services.AddScoped<QueueTestDeliveryHandler>();
        services.AddScoped<QueueDeliveryHandler>();
        services.AddScoped<DispatchDeliveriesHandler>();
        services.AddScoped<WorkItemWebhookService>();
        services.AddScoped<IWorkItemWebhookDelivery>(provider =>
            new WorkItemWebhookDeliveryAdapter(
                provider.GetRequiredService<QueueDeliveryHandler>()));
        return services;
    }
}
