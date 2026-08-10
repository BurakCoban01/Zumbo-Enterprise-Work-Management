using Zumbo.Modules.Notifications.Application.Policies;

namespace Zumbo.Modules.Notifications;

internal static class NotificationResponseMapper
{
    internal static NotificationResponse ToResponse(NotificationDocument notification)
    {
        var presentation = NotificationPresentationPolicy.For(
            notification.Type,
            notification.SourceKind,
            notification.SourceId,
            notification.DeduplicationKey);
        return new(
            notification.Id,
            notification.UserId,
            notification.Type,
            notification.Message,
            notification.Read,
            notification.EmailStatus,
            notification.CreatedAt,
            presentation.Category,
            presentation.ActionKind,
            presentation.SourceKind,
            presentation.SourceId,
            notification.ProjectId);
    }
}
