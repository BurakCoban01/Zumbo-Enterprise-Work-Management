using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Notifications;

internal sealed class GetNotificationDeliveryMetricsSlice(
    IDocumentRepository<NotificationDocument> notifications,
    IClock clock)
{
    internal async Task<NotificationDeliveryMetrics> HandleAsync(
        GetNotificationDeliveryMetricsQuery query,
        CancellationToken ct)
    {
        var organizationId = NotificationDeliveryPolicy.RequireOrganizationId(
            query.OrganizationId);
        var pending = await notifications.CountByFilterAsync(
            x => x.OrganizationId == organizationId
                && x.EmailStatus == NotificationEmailStatuses.Pending,
            ct);
        var processing = await notifications.CountByFilterAsync(
            x => x.OrganizationId == organizationId
                && x.EmailStatus == NotificationEmailStatuses.Processing,
            ct);
        var sent = await notifications.CountByFilterAsync(
            x => x.OrganizationId == organizationId
                && x.EmailStatus == NotificationEmailStatuses.Sent,
            ct);
        var deadLetter = await notifications.CountByFilterAsync(
            x => x.OrganizationId == organizationId
                && x.EmailStatus == NotificationEmailStatuses.DeadLetter,
            ct);
        var disabled = await notifications.CountByFilterAsync(
            x => x.OrganizationId == organizationId
                && x.EmailStatus == NotificationEmailStatuses.Disabled,
            ct);
        var oldest = (await notifications.ListByFilterAsync(
            x => x.OrganizationId == organizationId
                && x.EmailStatus == NotificationEmailStatuses.Pending,
            x => x.EmailNextAttemptAt!,
            pageSize: 1,
            cancellationToken: ct)).SingleOrDefault();
        return new NotificationDeliveryMetrics(
            organizationId,
            pending,
            processing,
            sent,
            deadLetter,
            disabled,
            oldest?.EmailNextAttemptAt,
            clock.UtcNow);
    }
}
