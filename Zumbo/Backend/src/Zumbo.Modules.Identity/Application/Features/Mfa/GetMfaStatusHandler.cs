using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Identity.Application.Features.Mfa;

public sealed class GetMfaStatusHandler(IUserRepository users, ICurrentUser currentUser)
{
    public async Task<MfaStatusResponse> HandleAsync(CancellationToken ct)
    {
        var userId = currentUser.UserId;
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new UnauthorizedException("Authenticated user is required.");
        }

        var user = await users.GetByIdAsync(userId, ct)
            ?? throw new UnauthorizedException("Authenticated user was not found.");
        return new MfaStatusResponse(user.MfaEnabled, user.MfaRecoveryCodeHashes.Count);
    }
}
