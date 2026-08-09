namespace Zumbo.Modules.Notifications;

internal static class NotificationPreferenceMapper
{
    internal static NotificationPreferenceResponse ToResponse(
        NotificationPreferenceDocument preference) =>
        new(
            preference.InAppEnabled,
            preference.EmailEnabled,
            preference.MutedTypes,
            preference.TypeSettings.Select(setting => new NotificationTypePreferenceResponse(
                setting.Type,
                setting.InAppEnabled,
                setting.EmailEnabled)).ToList(),
            preference.Version,
            preference.DeliveryMode,
            preference.TimeZoneId,
            preference.DigestHourLocal);
}
