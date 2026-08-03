using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Notifications;

public sealed class ListNotificationsHandler(NotificationService service)
{
    private ListNotificationsSlice? slice;

    public ListNotificationsHandler(
        IDocumentRepository<NotificationDocument> notifications,
        ICurrentUser currentUser)
        : this(null!)
    {
        slice = new ListNotificationsSlice(notifications, currentUser);
    }

    public Task<IReadOnlyList<NotificationResponse>> HandleAsync(ListNotificationsQuery query, CancellationToken ct) =>
        slice?.HandleAsync(query, ct)
        ?? service.ListAsync(query.UserId, ct, query.Page, query.PageSize, query.UnreadOnly);
}
