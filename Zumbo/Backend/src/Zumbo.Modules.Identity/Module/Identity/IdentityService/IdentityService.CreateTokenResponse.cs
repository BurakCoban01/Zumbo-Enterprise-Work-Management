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
}
