using Zumbo.BuildingBlocks.Application.Persistence;

namespace Zumbo.Modules.WorkItems;

public sealed class DevelopmentWebhookReceiptDocument : IVersionedDocument
{
    public string Id { get; set; } = string.Empty;
    public string OrganizationId { get; set; } = string.Empty;
    public string ConnectionId { get; set; } = string.Empty;
    public string DeliveryId { get; set; } = string.Empty;
    public string ProviderEvent { get; set; } = string.Empty;
    public string PayloadSha256 { get; set; } = string.Empty;
    public string Status { get; set; } = DevelopmentWebhookReceiptStatuses.Pending;
    public int AppliedLinks { get; set; }
    public DateTimeOffset ReceivedAtUtc { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
    public long Version { get; set; }
}
