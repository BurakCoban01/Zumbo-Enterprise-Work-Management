namespace Zumbo.Modules.WorkItems.Application.Features.Webhooks.Deliveries;

internal static class DeliveryResponseMapper
{
    public static WebhookDeliveryResponse ToResponse(WebhookDeliveryDocument document) => new(
        document.Id,
        document.SubscriptionId,
        document.EventScope,
        document.PayloadSha256,
        document.Status,
        document.Attempts,
        document.NextAttemptAtUtc,
        document.LastErrorCode,
        document.DeliveredAtUtc,
        document.DeadLetteredAtUtc,
        document.CreatedAtUtc,
        document.Version);
}
