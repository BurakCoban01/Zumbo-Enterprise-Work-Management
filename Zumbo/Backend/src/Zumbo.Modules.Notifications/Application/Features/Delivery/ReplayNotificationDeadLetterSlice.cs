using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Notifications;

internal sealed class ReplayNotificationDeadLetterSlice(
    IDocumentRepository<NotificationDocument> notifications,
    IClock clock)
{
    internal async Task<bool> HandleAsync(
        ReplayNotificationDeadLetterCommand command,
        CancellationToken ct)
    {
        var organizationId = NotificationDeliveryPolicy.RequireOrganizationId(
            command.OrganizationId);
        var notification = await notifications.SelectAsync(
            x => x.Id == command.NotificationId
                && x.OrganizationId == organizationId
                && x.EmailStatus == NotificationEmailStatuses.DeadLetter,
            ct);
        if (notification is null) return false;
        notification.EmailStatus = NotificationEmailStatuses.Pending;
        notification.EmailAttempts = 0;
        notification.EmailNextAttemptAt = clock.UtcNow;
        notification.EmailDeadLetteredAt = null;
        notification.EmailLastError = null;
        NotificationDeliveryPolicy.ClearLease(notification);
        var result = await notifications.ReplaceByFilterAsync(
            x => x.Id == command.NotificationId
                && x.OrganizationId == organizationId
                && x.EmailStatus == NotificationEmailStatuses.DeadLetter,
            notification,
            ct);
        return result.MatchedCount == 1;
    }
}
