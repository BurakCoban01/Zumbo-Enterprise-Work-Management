using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Notifications;

public sealed partial class NotificationService{

    public async Task MarkAsReadAsync(string notificationId, CancellationToken ct)
    {
        await markNotificationAsReadHandler.HandleAsync(
            new MarkNotificationAsReadCommand(notificationId),
            ct);
    }
}
