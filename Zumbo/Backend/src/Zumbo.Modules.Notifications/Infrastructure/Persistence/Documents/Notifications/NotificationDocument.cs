using Zumbo.BuildingBlocks.Application.Persistence;

namespace Zumbo.Modules.Notifications;

public sealed class NotificationDocument : IDocument
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string OrganizationId { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string? SourceKind { get; set; }
    public string? SourceId { get; set; }
    public string? ProjectId { get; set; }
    public bool Read { get; set; }
    public string? EmailAddress { get; set; }
    public string EmailStatus { get; set; } = NotificationEmailStatuses.Disabled;
    public string EmailDeliveryMode { get; set; } = NotificationDeliveryModes.Immediate;
    public int EmailAttempts { get; set; }
    public DateTimeOffset? EmailNextAttemptAt { get; set; }
    public DateTimeOffset? EmailSentAt { get; set; }
    public string? EmailLastError { get; set; }
    public string? EmailLeaseToken { get; set; }
    public string? EmailClaimedBy { get; set; }
    public DateTimeOffset? EmailLeaseUntil { get; set; }
    public DateTimeOffset? EmailDeadLetteredAt { get; set; }
    public string? DeduplicationKey { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
