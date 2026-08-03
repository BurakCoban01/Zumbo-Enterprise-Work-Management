namespace Zumbo.Modules.Notifications;

internal static class NotificationResponseMapper
{
    internal static NotificationResponse ToResponse(NotificationDocument notification) =>
        new(
            notification.Id,
            notification.UserId,
            notification.Type,
            notification.Message,
            notification.Read,
            notification.EmailStatus,
            notification.CreatedAt);
}
