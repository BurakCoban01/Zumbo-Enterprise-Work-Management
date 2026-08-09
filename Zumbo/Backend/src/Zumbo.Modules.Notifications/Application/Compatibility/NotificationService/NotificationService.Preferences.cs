using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Notifications;

public sealed partial class NotificationService
{
    public async Task<NotificationPreferenceResponse> GetPreferencesAsync(CancellationToken ct)
        => await new GetNotificationPreferencesHandler(
            preferences,
            distributedLockProvider,
            distributedLockOptions,
            currentUser).HandleAsync(new GetNotificationPreferencesQuery(), ct);

    public async Task<NotificationPreferenceResponse> UpdatePreferencesAsync(
        UpdateNotificationPreferencesRequest request,
        CancellationToken ct)
        => await new UpdateNotificationPreferencesHandler(
            preferences,
            distributedLockProvider,
            distributedLockOptions,
            clock,
            currentUser).HandleAsync(
                new UpdateNotificationPreferencesCommand(request), ct);

    private void EnsureCurrentUser(string userId)
    {
        if (!string.Equals(RequireCurrentUser(), userId, StringComparison.Ordinal))
        {
            throw new ForbiddenException("Users can only access their own notifications.");
        }
    }

    private string RequireCurrentUser() =>
        currentUser.UserId ?? throw new UnauthorizedException("Authenticated user is required.");

    private static string NormalizeDeliveryMode(string value)
    {
        var normalized = string.IsNullOrWhiteSpace(value)
            ? NotificationDeliveryModes.Immediate
            : value.Trim();
        if (normalized.Equals(NotificationDeliveryModes.Immediate, StringComparison.OrdinalIgnoreCase))
            return NotificationDeliveryModes.Immediate;
        if (normalized.Equals(NotificationDeliveryModes.DailyDigest, StringComparison.OrdinalIgnoreCase))
            return NotificationDeliveryModes.DailyDigest;
        throw new ValidationException("Notification delivery mode must be Immediate or DailyDigest.");
    }

    private static string NormalizeTimeZone(string value)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? "UTC" : value.Trim();
        try
        {
            _ = TimeZoneInfo.FindSystemTimeZoneById(normalized);
            return normalized;
        }
        catch (TimeZoneNotFoundException)
        {
            throw new ValidationException("Notification time zone is invalid.");
        }
        catch (InvalidTimeZoneException)
        {
            throw new ValidationException("Notification time zone is invalid.");
        }
    }
}
