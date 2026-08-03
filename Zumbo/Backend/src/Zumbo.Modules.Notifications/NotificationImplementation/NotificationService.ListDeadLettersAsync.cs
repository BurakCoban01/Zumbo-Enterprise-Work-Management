using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Notifications;

public sealed partial class NotificationService{

    public async Task<IReadOnlyList<NotificationDeadLetterSummary>> ListDeadLettersAsync(
        string organizationId,
        int pageSize,
        CancellationToken ct)
    {
        organizationId = RequireOrganizationId(organizationId);
        if (pageSize is < 1 or > 50)
        {
            throw new ValidationException("Notification dead-letter page size must be between 1 and 50.");
        }

        var items = await notifications.ListByFilterAsync(
            x => x.OrganizationId == organizationId
                && x.EmailStatus == NotificationEmailStatuses.DeadLetter,
            x => x.EmailDeadLetteredAt!,
            orderDescending: true,
            pageSize: pageSize,
            cancellationToken: ct);
        return items.Select(item => new NotificationDeadLetterSummary(
            item.Id,
            item.Type,
            item.EmailAttempts,
            item.EmailDeadLetteredAt ?? item.CreatedAt)).ToList();
    }
}
