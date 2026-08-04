using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Identity.Application.Features.SessionManagement;

public sealed class ListSessionsHandler(
    IUserRepository users,
    IRefreshSessionStore sessions,
    ICurrentUser currentUser)
{
    public async Task<IReadOnlyList<SessionResponse>> HandleAsync(
        string? currentSessionId,
        CancellationToken ct)
    {
        var user = await GetCurrentUserAsync(ct);
        var ownedSessions = await sessions.ListOwnedAsync(user.Id, user.OrganizationId, ct);
        return ownedSessions
            .Select(session => new SessionResponse(
                session.Id,
                session.DeviceName,
                session.ClientFingerprint,
                session.CreatedAt,
                session.LastSeenAt == default ? session.CreatedAt : session.LastSeenAt,
                session.ExpiresAt,
                session.RevokedAt,
                string.Equals(session.Id, currentSessionId, StringComparison.Ordinal)))
            .ToList();
    }

    private async Task<UserDocument> GetCurrentUserAsync(CancellationToken ct)
    {
        var userId = currentUser.UserId;
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new UnauthorizedException("Authenticated user is required.");
        }

        return await users.GetByIdAsync(userId, ct)
            ?? throw new UnauthorizedException("Authenticated user was not found.");
    }
}
