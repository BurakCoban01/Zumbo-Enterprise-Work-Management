namespace Zumbo.Modules.WorkItems.Application.Features.Webhooks.Deliveries;

public sealed record QueueTestDeliveryCommand(
    string SubscriptionId,
    string? CorrelationId);
