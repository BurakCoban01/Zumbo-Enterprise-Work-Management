using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Notifications;

public sealed class MarkNotificationAsReadHandler(NotificationService service)
{
    private MarkNotificationAsReadSlice? slice;

    public MarkNotificationAsReadHandler(
        IDocumentRepository<NotificationDocument> notifications,
        ICurrentUser currentUser)
        : this(null!)
    {
        slice = new MarkNotificationAsReadSlice(notifications, currentUser);
    }

    public async Task<MarkNotificationAsReadResponse> HandleAsync(
        MarkNotificationAsReadCommand command,
        CancellationToken ct)
    {
        if (slice is not null)
        {
            return await slice.HandleAsync(command, ct);
        }

        MarkNotificationAsReadValidator.Validate(command);
        await service.MarkAsReadAsync(command.NotificationId, ct);
        return new MarkNotificationAsReadResponse(true);
    }
}
