using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Notifications;

public sealed partial class NotificationService{

    public async Task MarkAsReadAsync(string notificationId, CancellationToken ct)
    {
        var userId = RequireCurrentUser();
        var notification = await notifications.SelectAsync(x => x.Id == notificationId && x.UserId == userId, ct)
            ?? throw new NotFoundException("NOTIFICATION_NOT_FOUND", "Notification was not found.");
        if (!notification.Read)
        {
            await notifications.UpdateOneFieldByFilterAsync(
                x => x.Id == notificationId && x.UserId == userId,
                x => x.Read,
                true,
                ct);
        }
    }
}
