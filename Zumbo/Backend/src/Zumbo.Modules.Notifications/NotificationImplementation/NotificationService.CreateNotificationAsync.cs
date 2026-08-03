using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Notifications;

public sealed partial class NotificationService{

    private async Task CreateNotificationAsync(
        NotificationUser user,
        string type,
        string message,
        NotificationPreferenceDocument? preference,
        string? deduplicationKey,
        CancellationToken ct)
    {
        var typeSetting = preference?.TypeSettings.SingleOrDefault(
            setting => setting.Type.Equals(type, StringComparison.OrdinalIgnoreCase));
        var inAppEnabled = (preference?.InAppEnabled ?? true)
            && (typeSetting?.InAppEnabled ?? true);
        var emailEnabled = (preference?.EmailEnabled ?? true)
            && (typeSetting?.EmailEnabled ?? true)
            && emailOptions.Value.Enabled;
        if (!inAppEnabled && !emailEnabled)
        {
            return;
        }

        var deliveryMode = preference?.DeliveryMode ?? NotificationDeliveryModes.Immediate;
        var nextAttempt = emailEnabled
            ? deliveryMode == NotificationDeliveryModes.DailyDigest
                ? NextDigestAt(clock.UtcNow, preference?.TimeZoneId ?? "UTC", preference?.DigestHourLocal ?? 8)
                : clock.UtcNow
            : (DateTimeOffset?)null;
        await notifications.CreateAsync(new NotificationDocument
        {
            OrganizationId = user.OrganizationId,
            UserId = user.Id,
            Type = type,
            Message = message,
            Read = !inAppEnabled,
            EmailAddress = emailEnabled ? user.Email : null,
            EmailStatus = emailEnabled ? NotificationEmailStatuses.Pending : NotificationEmailStatuses.Disabled,
            EmailDeliveryMode = deliveryMode,
            EmailNextAttemptAt = nextAttempt,
            DeduplicationKey = deduplicationKey,
            CreatedAt = clock.UtcNow
        }, ct);
    }
}
