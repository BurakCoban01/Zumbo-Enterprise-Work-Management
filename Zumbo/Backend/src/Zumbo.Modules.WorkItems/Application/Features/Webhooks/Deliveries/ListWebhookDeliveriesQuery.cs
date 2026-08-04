namespace Zumbo.Modules.WorkItems.Application.Features.Webhooks.Deliveries;

public sealed record ListWebhookDeliveriesQuery(
    string SubscriptionId,
    string? Cursor,
    int PageSize);
