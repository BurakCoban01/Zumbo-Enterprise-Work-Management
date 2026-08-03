using Zumbo.SharedKernel;

namespace Zumbo.Modules.Notifications;

public sealed class ListNotificationsHandler(NotificationService service)
{
    public Task<IReadOnlyList<NotificationResponse>> HandleAsync(ListNotificationsQuery query, CancellationToken ct) =>
        service.ListAsync(query.UserId, ct, query.Page, query.PageSize, query.UnreadOnly);
}
