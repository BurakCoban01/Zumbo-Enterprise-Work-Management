using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Notifications;

internal sealed class ListNotificationDeadLettersSlice(
    IDocumentRepository<NotificationDocument> notifications)
{
    internal async Task<IReadOnlyList<NotificationDeadLetterSummary>> HandleAsync(
        ListNotificationDeadLettersQuery query,
        CancellationToken ct)
    {
        var organizationId = NotificationDeliveryPolicy.RequireOrganizationId(
            query.OrganizationId);
        if (query.PageSize is < 1 or > 50)
        {
            throw new ValidationException(
                "Notification dead-letter page size must be between 1 and 50.");
        }
        var items = await notifications.ListByFilterAsync(
            x => x.OrganizationId == organizationId
                && x.EmailStatus == NotificationEmailStatuses.DeadLetter,
            x => x.EmailDeadLetteredAt!,
            orderDescending: true,
            pageSize: query.PageSize,
            cancellationToken: ct);
        return items.Select(item => new NotificationDeadLetterSummary(
            item.Id,
            item.Type,
            item.EmailAttempts,
            item.EmailDeadLetteredAt ?? item.CreatedAt)).ToList();
    }
}
