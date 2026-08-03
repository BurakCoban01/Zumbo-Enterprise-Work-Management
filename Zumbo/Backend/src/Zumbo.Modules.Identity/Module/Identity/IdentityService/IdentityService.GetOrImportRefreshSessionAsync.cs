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
}
