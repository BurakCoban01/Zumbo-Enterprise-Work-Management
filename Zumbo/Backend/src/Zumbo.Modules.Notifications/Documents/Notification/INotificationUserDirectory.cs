using Zumbo.BuildingBlocks.Application.Persistence;

namespace Zumbo.Modules.Notifications;

public interface INotificationUserDirectory
{
    Task<NotificationUser?> FindAsync(string userId, CancellationToken ct);
}
