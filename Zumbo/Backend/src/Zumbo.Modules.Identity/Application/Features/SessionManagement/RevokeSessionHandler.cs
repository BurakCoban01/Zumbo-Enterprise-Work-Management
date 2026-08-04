using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Identity.Application.Features.SessionManagement;

public sealed class RevokeSessionHandler(
    IUserRepository users,
    IRefreshSessionStore sessions,
    IClock clock,
    ICurrentUser currentUser,
    IIdentityAuditWriter? audit = null)
{
    public async Task HandleAsync(string sessionId, string correlationId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(sessionId) || sessionId.Length > 64)
        {
            throw new NotFoundException("SESSION_NOT_FOUND", "Session was not found.");
        }

        var user = await GetCurrentUserAsync(ct);
        DocumentConcurrencyException? lastConflict = null;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var session = await sessions.GetByIdAsync(sessionId, user.Id, user.OrganizationId, ct)
                ?? throw new NotFoundException("SESSION_NOT_FOUND", "Session was not found.");
            if (session.RevokedAt is not null)
            {
                return;
            }

            try
            {
                if (!await sessions.RevokeAsync(session, clock.UtcNow, null, ct))
                {
                    continue;
                }

                await WriteAuditAsync("SessionRevoked", user.Id, session.Id, null, correlationId, ct);
                return;
            }
            catch (DocumentConcurrencyException conflict)
            {
                lastConflict = conflict;
            }
        }

        if (lastConflict is not null)
        {
            throw lastConflict;
        }

        throw new ConflictException("SESSION_REVOKE_CONFLICT", "Session could not be revoked; retry the operation.");
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

    private Task WriteAuditAsync(
        string action,
        string entityId,
        string? oldValue,
        string? newValue,
        string correlationId,
        CancellationToken ct) =>
        audit?.WriteAsync(action, entityId, oldValue, newValue, correlationId, ct)
        ?? Task.CompletedTask;
}
