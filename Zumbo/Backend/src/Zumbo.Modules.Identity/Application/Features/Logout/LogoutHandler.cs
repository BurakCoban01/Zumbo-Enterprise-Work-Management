using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Identity.Application.Features.Logout;

public sealed class LogoutHandler(
    IUserRepository users,
    IRefreshSessionStore sessions,
    IDurableTransactionRunner transactions,
    IClock clock)
{
    public async Task<LogoutResponse> HandleAsync(LogoutRequest request, CancellationToken ct)
    {
        DocumentConcurrencyException? lastConflict = null;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                return await transactions.ExecuteAsync(
                    "Identity",
                    token => LogoutAttemptAsync(request, token),
                    ct);
            }
            catch (DocumentConcurrencyException conflict)
            {
                lastConflict = conflict;
            }
        }

        throw lastConflict!;
    }

    private async Task<LogoutResponse> LogoutAttemptAsync(LogoutRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.RefreshToken))
        {
            throw new ValidationException("Refresh token is required.");
        }

        var target = await GetOrImportRefreshSessionAsync(request.RefreshToken, ct);
        if (target is null)
        {
            return new LogoutResponse(true, 0);
        }

        var now = clock.UtcNow;
        var canRevokeAll = request.AllSessions && target.IsActive(now);
        var revokedSessions = await RevokeSessionAsync(target, now, ct);
        if (canRevokeAll)
        {
            revokedSessions += await sessions.RevokeAllAsync(target.UserId, target.OrganizationId, now, ct);
            var user = await users.GetByIdAsync(target.UserId, ct);
            if (user is not null && user.OrganizationId == target.OrganizationId
                && LegacyRefreshSessionCompatibility.RevokeAll(user, now))
            {
                await users.UpdateAsync(user, ct);
            }
        }

        return new LogoutResponse(true, revokedSessions);
    }

    private async Task<RefreshSessionDocument?> GetOrImportRefreshSessionAsync(string rawToken, CancellationToken ct)
    {
        var stored = await sessions.GetByTokenAsync(rawToken, ct);
        if (stored is not null)
        {
            return stored;
        }

        var legacyUser = await users.GetByRefreshTokenAsync(rawToken, ct);
        var tokenHash = RefreshTokenSecurity.Hash(rawToken);
        var legacy = legacyUser?.RefreshTokens.SingleOrDefault(x => x.TokenHash == tokenHash);
        if (legacyUser is null || legacy is null)
        {
            return null;
        }

        var imported = new RefreshSessionDocument
        {
            Id = legacy.SessionId,
            UserId = legacyUser.Id,
            OrganizationId = legacyUser.OrganizationId,
            TokenHash = legacy.TokenHash,
            CreatedAt = legacy.CreatedAt,
            LastSeenAt = legacy.CreatedAt,
            DeviceName = "Legacy session",
            ExpiresAt = legacy.ExpiresAt,
            ExpiresAtUtc = legacy.ExpiresAt.UtcDateTime,
            RevokedAt = legacy.RevokedAt,
            RetainUntilUtc = (legacy.RevokedAt ?? legacy.ExpiresAt).AddDays(30).UtcDateTime
        };
        try
        {
            await sessions.CreateAsync(imported, ct);
            return imported;
        }
        catch (DocumentConflictException)
        {
            return await sessions.GetByTokenAsync(rawToken, ct);
        }
    }

    private async Task<int> RevokeSessionAsync(RefreshSessionDocument? session, DateTimeOffset now, CancellationToken ct)
    {
        if (session is null || !session.IsActive(now))
        {
            return 0;
        }

        return await sessions.RevokeAsync(session, now, null, ct) ? 1 : 0;
    }
}
