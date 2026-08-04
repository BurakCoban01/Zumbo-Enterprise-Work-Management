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

    public Task<IReadOnlyList<SessionResponse>> ListSessionsAsync(CancellationToken ct) =>
        ListSessionsAsync(null, ct);

    public async Task<IReadOnlyList<SessionResponse>> ListSessionsAsync(string? currentSessionId, CancellationToken ct)
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
}
