namespace Zumbo.Modules.Notifications;

public sealed record CreateNotificationCommand(
    string UserId,
    string Type,
    string Message,
    string? DeduplicationKey = null,
    string? SourceKind = null,
    string? SourceId = null,
    string? ProjectId = null);
