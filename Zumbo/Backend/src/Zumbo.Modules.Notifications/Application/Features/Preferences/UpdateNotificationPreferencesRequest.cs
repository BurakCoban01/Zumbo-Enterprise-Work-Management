using Zumbo.BuildingBlocks.Application.Persistence;

namespace Zumbo.Modules.Notifications;

public sealed record UpdateNotificationPreferencesRequest(
    bool InAppEnabled,
    bool EmailEnabled,
    IReadOnlyCollection<string>? MutedTypes,
    IReadOnlyCollection<NotificationTypePreferenceRequest>? TypeSettings = null,
    string DeliveryMode = NotificationDeliveryModes.Immediate,
    string TimeZoneId = "UTC",
    int DigestHourLocal = 8);
