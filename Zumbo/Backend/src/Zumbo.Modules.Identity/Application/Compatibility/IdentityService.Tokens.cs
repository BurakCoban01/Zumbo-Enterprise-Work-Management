using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Identity;

public sealed partial class IdentityService
{
    public async Task<AuthResponse> RefreshAsync(RefreshTokenRequest request, CancellationToken ct) =>
            await refreshTokenHandler.HandleAsync(request, ct);

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

    private async Task<RefreshSessionDocument?> GetOrImportRefreshSessionAsync(
            string rawToken,
            CancellationToken ct)
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

    private async Task<AuthResponse> IssueTokensAsync(
            UserDocument user,
            DateTimeOffset now,
            CancellationToken ct)
        {
            _ = await sessions.PurgeRetainedAsync(now, 100, ct);
            var created = CreateTokenResponse(user, now);
            await sessions.CreateAsync(created.Session, ct);
            return created.Response;
        }

    private TokenResponseWithSession CreateTokenResponse(
            UserDocument user,
            DateTimeOffset now,
            RefreshSessionDocument? previousSession = null)
        {
            var options = jwtOptions.Value;
            var rawRefreshToken = tokenIssuer.CreateRefreshToken();
            var client = GetSessionClientInfo(previousSession);
            var refreshSession = new RefreshSessionDocument
            {
                UserId = user.Id,
                OrganizationId = user.OrganizationId,
                TokenHash = RefreshTokenSecurity.Hash(rawRefreshToken),
                CreatedAt = now,
                LastSeenAt = now,
                DeviceName = client.DeviceName,
                ClientFingerprint = client.ClientFingerprint,
                ExpiresAt = now.AddDays(14),
                ExpiresAtUtc = now.AddDays(14).UtcDateTime,
                RetainUntilUtc = now.AddDays(44).UtcDateTime
            };
            var accessToken = tokenIssuer.CreateAccessToken(
                new TokenUser(
                    user.Id,
                    user.Username,
                    user.Email,
                    user.OrganizationId,
                    user.Roles,
                    user.SecurityStamp,
                    refreshSession.Id),
                options,
                now);

            return new TokenResponseWithSession(
                new AuthResponse(
                    accessToken,
                    rawRefreshToken,
                    now.AddMinutes(options.AccessTokenMinutes),
                    IdentityMappings.ToProfile(user)),
                refreshSession);
        }

    private sealed record RefreshAttemptResult(AuthResponse? Response, bool ReuseDetected)
        {
            public static RefreshAttemptResult Reused { get; } = new(null, true);
        }

    private sealed record TokenResponseWithSession(
            AuthResponse Response,
            RefreshSessionDocument Session);

    private SessionClientInfo GetSessionClientInfo(RefreshSessionDocument? previousSession)
        {
            var supplied = sessionClientContext?.GetClientInfo();
            var deviceName = NormalizeDeviceName(supplied?.DeviceName)
                ?? NormalizeDeviceName(previousSession?.DeviceName)
                ?? "Unknown client";
            var fingerprint = supplied?.ClientFingerprint?.Trim();
            if (string.IsNullOrWhiteSpace(fingerprint) || fingerprint.Length > 128)
            {
                fingerprint = previousSession?.ClientFingerprint ?? string.Empty;
            }

            return new SessionClientInfo(deviceName, fingerprint);
        }
}
