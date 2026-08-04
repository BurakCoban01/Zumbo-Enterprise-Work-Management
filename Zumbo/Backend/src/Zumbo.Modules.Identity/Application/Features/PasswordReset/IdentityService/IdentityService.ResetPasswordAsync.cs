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

    public Task<PasswordResetResponse> ResetPasswordAsync(ResetPasswordRequest request, CancellationToken ct) =>
        ResetPasswordAsync(request, "system", ct);

    public async Task<PasswordResetResponse> ResetPasswordAsync(
        ResetPasswordRequest request,
        string correlationId,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Token) || request.Token.Length > 256)
        {
            throw new UnauthorizedException("Password reset token is invalid or expired.");
        }

        GuardPassword(request.NewPassword);
        var candidate = await users.GetByPasswordResetTokenAsync(request.Token, ct)
            ?? throw new UnauthorizedException("Password reset token is invalid or expired.");
        var user = await users.GetByIdAsync(candidate.Id, ct)
            ?? throw new UnauthorizedException("Password reset token is invalid or expired.");
        var tokenHash = RefreshTokenSecurity.Hash(request.Token);
        var now = clock.UtcNow;
        if (!user.IsActive
            || user.PasswordResetTokenExpiresAt <= now
            || !string.Equals(user.PasswordResetTokenHash, tokenHash, StringComparison.Ordinal))
        {
            throw new UnauthorizedException("Password reset token is invalid or expired.");
        }

        if (passwordHasher.Verify(request.NewPassword, user.PasswordHash))
        {
            throw new ConflictException("PASSWORD_UNCHANGED", "New password must be different from the current password.");
        }

        user.PasswordHash = passwordHasher.Hash(request.NewPassword);
        user.PasswordResetTokenHash = null;
        user.PasswordResetTokenExpiresAt = null;
        user.SecurityStamp = Guid.NewGuid().ToString("N");
        user.FailedLoginCount = 0;
        user.LockedUntil = null;
        LegacyRefreshSessionCompatibility.RevokeAll(user, now);
        try
        {
            await transactions.ExecuteAsync(
                "Identity",
                async token =>
                {
                    await sessions.RevokeAllAsync(user.Id, user.OrganizationId, now, token);
                    await users.UpdateAsync(user, token);
                },
                ct);
        }
        catch (DocumentConcurrencyException)
        {
            throw new UnauthorizedException("Password reset token is invalid or expired.");
        }

        await WriteAuditAsync("PasswordReset", user.Id, null, null, correlationId, ct);
        return new PasswordResetResponse(true);
    }
}
