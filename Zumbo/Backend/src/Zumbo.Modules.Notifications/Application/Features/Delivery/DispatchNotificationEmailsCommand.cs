namespace Zumbo.Modules.Notifications;

public sealed record DispatchNotificationEmailsCommand(
    int BatchSize,
    string? WorkerId = null);
