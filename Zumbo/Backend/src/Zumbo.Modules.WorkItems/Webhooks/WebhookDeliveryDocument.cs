using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed class WebhookDeliveryDocument : IVersionedDocument
{
    public string Id { get; set; } = string.Empty;
    public string OrganizationId { get; set; } = string.Empty;
    public string SubscriptionId { get; set; } = string.Empty;
    public string SourceEventId { get; set; } = string.Empty;
    public string EventScope { get; set; } = string.Empty;
    public string TargetUrl { get; set; } = string.Empty;
    public string Payload { get; set; } = string.Empty;
    public string PayloadSha256 { get; set; } = string.Empty;
    public string Status { get; set; } = WebhookDeliveryStatuses.Pending;
    public int Attempts { get; set; }
    public DateTimeOffset NextAttemptAtUtc { get; set; }
    public string? LeaseToken { get; set; }
    public string? ClaimedBy { get; set; }
    public DateTimeOffset? LeaseUntilUtc { get; set; }
    public string? LastErrorCode { get; set; }
    public DateTimeOffset? DeliveredAtUtc { get; set; }
    public DateTimeOffset? DeadLetteredAtUtc { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public long Version { get; set; }
}
