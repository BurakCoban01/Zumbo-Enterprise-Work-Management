using Zumbo.SharedKernel;

namespace Zumbo.Modules.Notifications;

public sealed record ListNotificationsQuery(
    string UserId,
    int Page = 1,
    int PageSize = 50,
    bool UnreadOnly = false);
