using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Notifications;

public sealed partial class NotificationService{

    public async Task<NotificationPreferenceResponse> UpdatePreferencesAsync(
        UpdateNotificationPreferencesRequest request,
        CancellationToken ct)
    {
        var userId = RequireCurrentUser();
        var mutedTypes = (request.MutedTypes ?? [])
            .Select(NormalizeType)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (mutedTypes.Count > 20)
        {
            throw new ValidationException("At most 20 notification types can be muted.");
        }

        var typeSettings = (request.TypeSettings ?? [])
            .Select(setting => new NotificationTypePreferenceDocument
            {
                Type = NormalizeType(setting.Type),
                InAppEnabled = setting.InAppEnabled,
                EmailEnabled = setting.EmailEnabled
            })
            .ToList();
        if (typeSettings.Count > 20
            || typeSettings.Select(setting => setting.Type).Distinct(StringComparer.OrdinalIgnoreCase).Count()
                != typeSettings.Count)
        {
            throw new ValidationException(
                "Notification type settings must contain at most 20 unique types.");
        }
        var deliveryMode = NormalizeDeliveryMode(request.DeliveryMode);
        var timeZoneId = NormalizeTimeZone(request.TimeZoneId);
        if (request.DigestHourLocal is < 0 or > 23)
            throw new ValidationException("Notification digest hour must be between 0 and 23.");

        await using var preferenceLock = await AcquireLockAsync("notification-preference:" + userId, ct);
        var existing = await preferences.SelectAsync(x => x.UserId == userId, ct);
        var preference = existing ?? new NotificationPreferenceDocument
        {
            Id = userId,
            UserId = userId
        };
        preference.InAppEnabled = request.InAppEnabled;
        preference.EmailEnabled = request.EmailEnabled;
        preference.MutedTypes = mutedTypes;
        preference.TypeSettings = typeSettings;
        preference.DeliveryMode = deliveryMode;
        preference.TimeZoneId = timeZoneId;
        preference.DigestHourLocal = request.DigestHourLocal;
        preference.UpdatedAt = clock.UtcNow;
        if (existing is null)
        {
            preference = await preferences.CreateAsync(preference, ct);
        }
        else
        {
            var result = await preferences.ReplaceByVersionAsync(
                x => x.Id == preference.Id,
                preference,
                preference.Version,
                ct);
            if (!result.Found)
            {
                throw new ConflictException(
                    "NOTIFICATION_PREFERENCE_CONFLICT",
                    "Notification preferences changed concurrently; retry the operation.");
            }
            preference.Version = result.Version!.Value;
        }

        return ToResponse(preference);
    }
}
