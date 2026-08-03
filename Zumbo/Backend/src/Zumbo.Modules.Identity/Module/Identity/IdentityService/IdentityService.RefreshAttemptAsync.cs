using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Identity;

public sealed partial class IdentityService{

    private async Task<RefreshAttemptResult> RefreshAttemptAsync(
        RefreshTokenRequest request,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.RefreshToken))
        {
            throw new ValidationException("Refresh token is required.");
        }

        var now = clock.UtcNow;
        var oldSession = await GetOrImportRefreshSessionAsync(request.RefreshToken, ct)
            ?? throw new UnauthorizedException("Refresh token is invalid.");
        var user = await users.GetByIdAsync(oldSession.UserId, ct)
            ?? throw new UnauthorizedException("Refresh token is invalid.");

        if (!user.IsActive || user.OrganizationId != oldSession.OrganizationId)
        {
            throw new ForbiddenException("User account is inactive.");
        }

        if (oldSession.RevokedAt is not null)
        {
            await sessions.RevokeAllAsync(user.Id, user.OrganizationId, now, ct);
            LegacyRefreshSessionCompatibility.RevokeAll(user, now);
            user.SecurityStamp = Guid.NewGuid().ToString("N");
            await users.UpdateAsync(user, ct);
            return RefreshAttemptResult.Reused;
        }

        if (oldSession.ExpiresAt <= now)
        {
            throw new UnauthorizedException("Refresh token is expired.");
        }

        var replacement = CreateTokenResponse(user, now, oldSession);
        if (!await sessions.RevokeAsync(oldSession, now, replacement.Session.Id, ct))
        {
            throw new UnauthorizedException("Refresh token is no longer active.");
        }

        await sessions.CreateAsync(replacement.Session, ct);
        return new RefreshAttemptResult(replacement.Response, false);
    }
}
