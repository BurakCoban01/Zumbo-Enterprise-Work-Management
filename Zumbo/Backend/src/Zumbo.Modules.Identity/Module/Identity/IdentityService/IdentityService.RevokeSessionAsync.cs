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

    public async Task RevokeSessionAsync(string sessionId, string correlationId, CancellationToken ct)
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

    private async Task<int> RevokeSessionAsync(
        RefreshSessionDocument? session,
        DateTimeOffset now,
        CancellationToken ct)
    {
        if (session is null || !session.IsActive(now))
        {
            return 0;
        }

        return await sessions.RevokeAsync(session, now, null, ct) ? 1 : 0;
    }
}
