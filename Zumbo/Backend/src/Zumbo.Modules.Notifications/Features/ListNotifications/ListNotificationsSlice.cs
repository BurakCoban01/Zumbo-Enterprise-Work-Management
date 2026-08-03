using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Notifications;

internal sealed class ListNotificationsSlice(
    IDocumentRepository<NotificationDocument> notifications,
    ICurrentUser currentUser)
{
    internal async Task<IReadOnlyList<NotificationResponse>> HandleAsync(
        ListNotificationsQuery query,
        CancellationToken ct)
    {
        EnsureCurrentUser(query.UserId);
        ListNotificationsValidator.Validate(query);

        var result = await notifications.ListByFilterAsync(
            x => x.UserId == query.UserId && (!query.UnreadOnly || !x.Read),
            x => x.CreatedAt,
            orderDescending: true,
            page: query.Page,
            pageSize: query.PageSize,
            cancellationToken: ct);
        return result.Select(NotificationResponseMapper.ToResponse).ToList();
    }

    private void EnsureCurrentUser(string userId)
    {
        var authenticatedUserId = currentUser.UserId
            ?? throw new UnauthorizedException("Authenticated user is required.");
        if (!string.Equals(authenticatedUserId, userId, StringComparison.Ordinal))
        {
            throw new ForbiddenException("Users can only access their own notifications.");
        }
    }
}
