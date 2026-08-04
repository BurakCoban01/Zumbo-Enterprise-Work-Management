namespace Zumbo.Modules.WorkItems.Application.Features.Webhooks.Subscriptions;

public sealed record SetSubscriptionStateCommand(
    string SubscriptionId,
    bool Active,
    SetWebhookSubscriptionStateRequest Request,
    string? CorrelationId);
