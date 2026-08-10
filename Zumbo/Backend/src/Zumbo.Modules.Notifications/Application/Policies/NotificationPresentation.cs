namespace Zumbo.Modules.Notifications.Application.Policies;

internal sealed record NotificationPresentation(
    string Category,
    string ActionKind,
    string? SourceKind,
    string? SourceId);
