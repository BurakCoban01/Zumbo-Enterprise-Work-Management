using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Notifications;

public sealed partial class NotificationService
{
    public async Task NotifyAsync(
        string userId,
        string type,
        string message,
        CancellationToken ct,
        string? deduplicationKey = null)
        => await new CreateNotificationHandler(
            notifications,
            preferences,
            userDirectory,
            emailOptions,
            distributedLockProvider,
            distributedLockOptions,
            clock).HandleAsync(
                new CreateNotificationCommand(
                    userId,
                    type,
                    message,
                    deduplicationKey),
                ct);

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

    private static string NormalizeType(string type)
    {
        if (string.IsNullOrWhiteSpace(type)) throw new ValidationException("Notification type is required.");
        var normalized = type.Trim();
        if (normalized.Length > 50) throw new ValidationException("Notification type cannot exceed 50 characters.");
        return normalized;
    }

    private static string NormalizeMessage(string message)
    {
        if (string.IsNullOrWhiteSpace(message)) throw new ValidationException("Notification message is required.");
        var normalized = message.Trim();
        if (normalized.Length > 2000) throw new ValidationException("Notification message cannot exceed 2000 characters.");
        return normalized;
    }

    private static string? NormalizeDeduplicationKey(string? value)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        if (normalized?.Length > 200)
            throw new ValidationException("Notification deduplication key cannot exceed 200 characters.");
        return normalized;
    }

    private static DateTimeOffset NextDigestAt(DateTimeOffset now, string timeZoneId, int hour)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        var localNow = TimeZoneInfo.ConvertTime(now, zone);
        var localTarget = new DateTime(
            localNow.Year, localNow.Month, localNow.Day, hour, 0, 0,
            DateTimeKind.Unspecified);
        if (localTarget <= localNow.DateTime) localTarget = localTarget.AddDays(1);
        if (zone.IsInvalidTime(localTarget)) localTarget = localTarget.AddHours(1);
        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(localTarget, zone), TimeSpan.Zero);
    }

    private async Task<IAsyncDisposable> AcquireLockAsync(string resource, CancellationToken ct)
    {
        var options = distributedLockOptions.Value;
        return await distributedLockProvider.TryAcquireAsync(
            resource,
            TimeSpan.FromSeconds(Math.Clamp(options.LeaseSeconds, 5, 300)),
            TimeSpan.FromSeconds(Math.Clamp(options.WaitSeconds, 0, 30)),
            ct)
            ?? throw new ConflictException("NOTIFICATION_RESOURCE_BUSY", "Notification resource is busy; retry the operation.");
    }
}
