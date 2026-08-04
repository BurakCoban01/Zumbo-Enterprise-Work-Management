namespace Zumbo.Modules.WorkItems;

public sealed record DevelopmentWebhookRequest(
    string DeliveryId,
    string EventName,
    string? Timestamp,
    string Signature,
    byte[] Payload);
