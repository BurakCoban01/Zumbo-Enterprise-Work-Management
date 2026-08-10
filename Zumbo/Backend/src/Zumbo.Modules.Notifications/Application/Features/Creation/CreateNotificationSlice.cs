using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

using Zumbo.Modules.Notifications.Application.Policies;

namespace Zumbo.Modules.Notifications;

internal sealed class CreateNotificationSlice(
    IDocumentRepository<NotificationDocument> notifications,
    IDocumentRepository<NotificationPreferenceDocument> preferences,
    INotificationUserDirectory userDirectory,
    EmailNotificationOptions emailOptions,
    NotificationCreationLockAccess lockAccess,
    IClock clock)
{
    internal async Task HandleAsync(
        CreateNotificationCommand command,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(command.UserId))
        {
            return;
        }

        var type = NotificationCreationPolicy.NormalizeType(command.Type);
        var message = NotificationCreationPolicy.NormalizeMessage(command.Message);
        var user = await userDirectory.FindAsync(command.UserId, ct);
        if (user is null || !user.IsActive)
        {
            return;
        }
        var preference = await preferences.SelectAsync(x => x.UserId == command.UserId, ct);
        if (preference?.MutedTypes.Contains(type, StringComparer.OrdinalIgnoreCase) == true)
        {
            return;
        }

        var deduplicationKey = NotificationCreationPolicy.NormalizeDeduplicationKey(
            command.DeduplicationKey);
        if (deduplicationKey is not null)
        {
            await using var dedupeLock = await lockAccess.AcquireAsync(
                $"notification-dedupe:{user.OrganizationId}:{deduplicationKey}", ct);
            if (await notifications.SelectAsync(
                    x => x.OrganizationId == user.OrganizationId
                        && x.DeduplicationKey == deduplicationKey,
                    ct) is not null)
            {
                return;
            }

            await CreateAsync(user, type, message, preference, deduplicationKey, command, ct);
            return;
        }

        await CreateAsync(user, type, message, preference, null, command, ct);
    }

    private async Task CreateAsync(
        NotificationUser user,
        string type,
        string message,
        NotificationPreferenceDocument? preference,
        string? deduplicationKey,
        CreateNotificationCommand command,
        CancellationToken ct)
    {
        var typeSetting = preference?.TypeSettings.SingleOrDefault(
            setting => setting.Type.Equals(type, StringComparison.OrdinalIgnoreCase));
        var inAppEnabled = (preference?.InAppEnabled ?? true)
            && (typeSetting?.InAppEnabled ?? true);
        var emailEnabled = (preference?.EmailEnabled ?? true)
            && (typeSetting?.EmailEnabled ?? true)
            && emailOptions.Enabled;
        if (!inAppEnabled && !emailEnabled)
        {
            return;
        }

        var deliveryMode = preference?.DeliveryMode ?? NotificationDeliveryModes.Immediate;
        var nextAttempt = emailEnabled
            ? deliveryMode == NotificationDeliveryModes.DailyDigest
                ? NotificationDigestSchedule.NextAt(
                    clock.UtcNow,
                    preference?.TimeZoneId ?? "UTC",
                    preference?.DigestHourLocal ?? 8)
                : clock.UtcNow
            : (DateTimeOffset?)null;
        await notifications.CreateAsync(new NotificationDocument
        {
            OrganizationId = user.OrganizationId,
            UserId = user.Id,
            Type = type,
            Message = message,
            SourceKind = NotificationPresentationPolicy.NormalizeSourceKind(command.SourceKind),
            SourceId = NotificationPresentationPolicy.NormalizeSourceId(command.SourceId),
            ProjectId = NotificationPresentationPolicy.NormalizeSourceId(command.ProjectId),
            Read = !inAppEnabled,
            EmailAddress = emailEnabled ? user.Email : null,
            EmailStatus = emailEnabled
                ? NotificationEmailStatuses.Pending
                : NotificationEmailStatuses.Disabled,
            EmailDeliveryMode = deliveryMode,
            EmailNextAttemptAt = nextAttempt,
            DeduplicationKey = deduplicationKey,
            CreatedAt = clock.UtcNow
        }, ct);
    }
}
