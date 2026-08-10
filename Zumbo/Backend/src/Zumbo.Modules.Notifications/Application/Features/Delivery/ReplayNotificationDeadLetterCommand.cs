namespace Zumbo.Modules.Notifications;

public sealed record ReplayNotificationDeadLetterCommand(
    string OrganizationId,
    string NotificationId);
