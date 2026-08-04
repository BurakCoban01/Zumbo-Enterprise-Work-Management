namespace Zumbo.Modules.WorkItems.Application.Features.Webhooks.Subscriptions;

public sealed record CreateSubscriptionCommand(
    CreateWebhookSubscriptionRequest Request,
    string? CorrelationId);
