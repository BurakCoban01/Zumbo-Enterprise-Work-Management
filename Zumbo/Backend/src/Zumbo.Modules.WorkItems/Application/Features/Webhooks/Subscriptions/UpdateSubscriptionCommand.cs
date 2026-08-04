namespace Zumbo.Modules.WorkItems.Application.Features.Webhooks.Subscriptions;

public sealed record UpdateSubscriptionCommand(
    string SubscriptionId,
    UpdateWebhookSubscriptionRequest Request,
    string? CorrelationId);
