using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Notifications;

public sealed partial class NotificationService{

    public async Task<IReadOnlyList<NotificationResponse>> ListAsync(
        string userId,
        CancellationToken ct,
        int page = 1,
        int pageSize = 50,
        bool unreadOnly = false)
        => await listNotificationsHandler.HandleAsync(
            new ListNotificationsQuery(userId, page, pageSize, unreadOnly),
            ct);
}
