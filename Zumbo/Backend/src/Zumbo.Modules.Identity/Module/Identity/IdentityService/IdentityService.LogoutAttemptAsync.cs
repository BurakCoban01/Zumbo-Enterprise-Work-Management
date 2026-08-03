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
}
