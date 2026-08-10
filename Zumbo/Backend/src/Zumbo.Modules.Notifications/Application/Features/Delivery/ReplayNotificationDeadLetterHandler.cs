using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Notifications;

public sealed class ReplayNotificationDeadLetterHandler(NotificationService service)
{
    private ReplayNotificationDeadLetterSlice? slice;

    public ReplayNotificationDeadLetterHandler(
        IDocumentRepository<NotificationDocument> notifications,
        IClock clock)
        : this(null!) =>
        slice = new ReplayNotificationDeadLetterSlice(notifications, clock);

    public Task<bool> HandleAsync(
        ReplayNotificationDeadLetterCommand command,
        CancellationToken ct) =>
        slice?.HandleAsync(command, ct)
        ?? service.ReplayDeadLetterAsync(
            command.OrganizationId,
            command.NotificationId,
            ct);
}
