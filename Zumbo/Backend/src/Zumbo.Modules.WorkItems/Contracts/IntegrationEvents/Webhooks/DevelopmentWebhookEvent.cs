namespace Zumbo.Modules.WorkItems;

public sealed record DevelopmentWebhookEvent(
    string ReceiptId,
    string ConnectionId,
    long ConnectionLifecycleVersion,
    string OrganizationId,
    string DeliveryId,
    string ProviderEvent,
    NormalizedDevelopmentEvent? Event);
