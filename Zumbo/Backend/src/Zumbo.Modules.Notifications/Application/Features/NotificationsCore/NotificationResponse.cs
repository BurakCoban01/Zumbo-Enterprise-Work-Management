using Zumbo.SharedKernel;

using Zumbo.Modules.Notifications.Application.Policies;

namespace Zumbo.Modules.Notifications;

public sealed record NotificationResponse(
    string Id,
    string UserId,
    string Type,
    string Message,
    bool Read,
    string EmailStatus,
    DateTimeOffset CreatedAt,
    string Category = NotificationCategories.Awareness,
    string ActionKind = NotificationActionKinds.None,
    string? SourceKind = null,
    string? SourceId = null,
    string? ProjectId = null);
