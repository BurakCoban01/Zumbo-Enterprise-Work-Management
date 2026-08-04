using Zumbo.BuildingBlocks.Application.Persistence;

namespace Zumbo.Modules.Notifications;

public sealed record NotificationPreferenceResponse(
    bool InAppEnabled,
    bool EmailEnabled,
    IReadOnlyCollection<string> MutedTypes,
    IReadOnlyCollection<NotificationTypePreferenceResponse>? TypeSettings = null,
    long Version = 0,
    string DeliveryMode = NotificationDeliveryModes.Immediate,
    string TimeZoneId = "UTC",
    int DigestHourLocal = 8);
