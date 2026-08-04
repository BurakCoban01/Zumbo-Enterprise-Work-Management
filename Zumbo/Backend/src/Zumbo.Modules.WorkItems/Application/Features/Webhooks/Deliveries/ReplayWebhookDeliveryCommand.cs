namespace Zumbo.Modules.WorkItems.Application.Features.Webhooks.Deliveries;

public sealed record ReplayWebhookDeliveryCommand(
    string DeliveryId,
    string? CorrelationId);
