using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Notifications;

internal sealed class MarkNotificationAsReadSlice(
    IDocumentRepository<NotificationDocument> notifications,
    ICurrentUser currentUser)
{
    internal async Task<MarkNotificationAsReadResponse> HandleAsync(
        MarkNotificationAsReadCommand command,
        CancellationToken ct)
    {
        MarkNotificationAsReadValidator.Validate(command);
        var userId = currentUser.UserId
            ?? throw new UnauthorizedException("Authenticated user is required.");
        var notification = await notifications.SelectAsync(
            x => x.Id == command.NotificationId && x.UserId == userId,
            ct)
            ?? throw new NotFoundException("NOTIFICATION_NOT_FOUND", "Notification was not found.");
        if (!notification.Read)
        {
            await notifications.UpdateOneFieldByFilterAsync(
                x => x.Id == command.NotificationId && x.UserId == userId,
                x => x.Read,
                true,
                ct);
        }

        return new MarkNotificationAsReadResponse(true);
    }
}
