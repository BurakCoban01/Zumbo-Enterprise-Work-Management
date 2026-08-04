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

    public Task<AuthResponse> ChangePasswordAsync(ChangePasswordRequest request, CancellationToken ct) =>
        ChangePasswordAsync(request, "system", ct);

    public async Task<AuthResponse> ChangePasswordAsync(
        ChangePasswordRequest request,
        string correlationId,
        CancellationToken ct)
    {
        var user = await GetCurrentUserAsync(ct);
        if (!user.IsActive)
        {
            throw new ForbiddenException("User account is inactive.");
        }

        if (string.IsNullOrWhiteSpace(request.CurrentPassword)
            || !passwordHasher.Verify(request.CurrentPassword, user.PasswordHash))
        {
            throw new UnauthorizedException("Current password is invalid.");
        }

        GuardPassword(request.NewPassword);
        if (passwordHasher.Verify(request.NewPassword, user.PasswordHash))
        {
            throw new ConflictException("PASSWORD_UNCHANGED", "New password must be different from the current password.");
        }

        var now = clock.UtcNow;
        var response = await transactions.ExecuteAsync(
            "Identity",
            async token =>
            {
                user.PasswordHash = passwordHasher.Hash(request.NewPassword);
                user.SecurityStamp = Guid.NewGuid().ToString("N");
                LegacyRefreshSessionCompatibility.RevokeAll(user, now);
                await sessions.RevokeAllAsync(user.Id, user.OrganizationId, now, token);
                await users.UpdateAsync(user, token);
                return await IssueTokensAsync(user, now, token);
            },
            ct);
        await WriteAuditAsync("PasswordChanged", user.Id, null, null, correlationId, ct);
        return response;
    }
}
