using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Identity.Application.Features.AccountLifecycle;

public sealed class DeactivateAccountHandler(
    IUserRepository users,
    IRefreshSessionStore sessions,
    IDurableTransactionRunner transactions,
    IPasswordHasher passwordHasher,
    IClock clock,
    ICurrentUser currentUser)
{
    public async Task<AccountStatusResponse> HandleAsync(
        DeactivateAccountRequest request,
        CancellationToken ct)
    {
        var userId = currentUser.UserId;
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new UnauthorizedException("Authenticated user is required.");
        }

        var user = await users.GetByIdAsync(userId, ct)
            ?? throw new UnauthorizedException("Authenticated user was not found.");
        if (string.IsNullOrWhiteSpace(request.Password)
            || !passwordHasher.Verify(request.Password, user.PasswordHash))
        {
            throw new UnauthorizedException("Password is invalid.");
        }

        var now = clock.UtcNow;
        user.IsActive = false;
        user.SecurityStamp = Guid.NewGuid().ToString("N");
        LegacyRefreshSessionCompatibility.RevokeAll(user, now);
        await transactions.ExecuteAsync(
            "Identity",
            async token =>
            {
                await sessions.RevokeAllAsync(user.Id, user.OrganizationId, now, token);
                await users.UpdateAsync(user, token);
            },
            ct);
        return new AccountStatusResponse(user.Id, false);
    }
}
