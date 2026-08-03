using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Notifications;

public sealed partial class NotificationService{
    public async Task NotifyAsync(
        string userId,
        string type,
        string message,
        CancellationToken ct,
        string? deduplicationKey = null)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            return;
        }

        var normalizedType = NormalizeType(type);
        var normalizedMessage = NormalizeMessage(message);
        var user = await userDirectory.FindAsync(userId, ct);
        if (user is null || !user.IsActive)
        {
            return;
        }
        var preference = await preferences.SelectAsync(x => x.UserId == userId, ct);
        if (preference?.MutedTypes.Contains(normalizedType, StringComparer.OrdinalIgnoreCase) == true)
        {
            return;
        }

        var normalizedDeduplicationKey = NormalizeDeduplicationKey(deduplicationKey);
        if (normalizedDeduplicationKey is not null)
        {
            await using var dedupeLock = await AcquireLockAsync(
                $"notification-dedupe:{user.OrganizationId}:{normalizedDeduplicationKey}", ct);
            if (await notifications.SelectAsync(
                    x => x.OrganizationId == user.OrganizationId
                        && x.DeduplicationKey == normalizedDeduplicationKey,
                    ct) is not null)
            {
                return;
            }

            await CreateNotificationAsync(
                user, normalizedType, normalizedMessage, preference, normalizedDeduplicationKey, ct);
            return;
        }

        await CreateNotificationAsync(user, normalizedType, normalizedMessage, preference, null, ct);
    }
}
