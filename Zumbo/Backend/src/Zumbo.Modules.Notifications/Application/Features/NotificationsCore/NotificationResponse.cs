using Zumbo.SharedKernel;

namespace Zumbo.Modules.Notifications;

public sealed record NotificationResponse(
    string Id,
    string UserId,
    string Type,
    string Message,
    bool Read,
    string EmailStatus,
    DateTimeOffset CreatedAt);
