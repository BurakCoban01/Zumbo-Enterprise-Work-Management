using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Notifications;

public sealed partial class NotificationService{

    public async Task<bool> ReplayDeadLetterAsync(
        string organizationId,
        string notificationId,
        CancellationToken ct)
    {
        organizationId = RequireOrganizationId(organizationId);
        var notification = await notifications.SelectAsync(
            x => x.Id == notificationId
                && x.OrganizationId == organizationId
                && x.EmailStatus == NotificationEmailStatuses.DeadLetter,
            ct);
        if (notification is null) return false;
        notification.EmailStatus = NotificationEmailStatuses.Pending;
        notification.EmailAttempts = 0;
        notification.EmailNextAttemptAt = clock.UtcNow;
        notification.EmailDeadLetteredAt = null;
        notification.EmailLastError = null;
        ClearLease(notification);
        var result = await notifications.ReplaceByFilterAsync(
            x => x.Id == notificationId
                && x.OrganizationId == organizationId
                && x.EmailStatus == NotificationEmailStatuses.DeadLetter,
            notification,
            ct);
        return result.MatchedCount == 1;
    }
}
