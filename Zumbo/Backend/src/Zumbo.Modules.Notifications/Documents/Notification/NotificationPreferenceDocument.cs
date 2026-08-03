using Zumbo.BuildingBlocks.Application.Persistence;

namespace Zumbo.Modules.Notifications;

public sealed class NotificationPreferenceDocument : IVersionedDocument
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string UserId { get; set; } = string.Empty;
    public bool InAppEnabled { get; set; } = true;
    public bool EmailEnabled { get; set; } = true;
    public List<string> MutedTypes { get; set; } = [];
    public List<NotificationTypePreferenceDocument> TypeSettings { get; set; } = [];
    public string DeliveryMode { get; set; } = NotificationDeliveryModes.Immediate;
    public string TimeZoneId { get; set; } = "UTC";
    public int DigestHourLocal { get; set; } = 8;
    public DateTimeOffset UpdatedAt { get; set; }
    public long Version { get; set; }
}
