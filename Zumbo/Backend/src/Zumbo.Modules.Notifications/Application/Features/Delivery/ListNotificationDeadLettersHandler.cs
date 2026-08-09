using Zumbo.BuildingBlocks.Application.Persistence;

namespace Zumbo.Modules.Notifications;

public sealed class ListNotificationDeadLettersHandler(NotificationService service)
{
    private ListNotificationDeadLettersSlice? slice;

    public ListNotificationDeadLettersHandler(
        IDocumentRepository<NotificationDocument> notifications)
        : this((NotificationService)null!) =>
        slice = new ListNotificationDeadLettersSlice(notifications);

    public Task<IReadOnlyList<NotificationDeadLetterSummary>> HandleAsync(
        ListNotificationDeadLettersQuery query,
        CancellationToken ct) =>
        slice?.HandleAsync(query, ct)
        ?? service.ListDeadLettersAsync(query.OrganizationId, query.PageSize, ct);
}
