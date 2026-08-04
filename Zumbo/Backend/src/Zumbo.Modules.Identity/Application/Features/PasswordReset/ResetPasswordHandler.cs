using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Identity.Application.Features.PasswordReset;

public sealed class ResetPasswordHandler(
    IUserRepository users,
    IRefreshSessionStore sessions,
    IDurableTransactionRunner transactions,
    IPasswordHasher passwordHasher,
    IClock clock,
    IIdentityAuditWriter? audit = null)
{
    public async Task<PasswordResetResponse> HandleAsync(
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

    private Task WriteAuditAsync(
        string action,
        string entityId,
        string? oldValue,
        string? newValue,
        string correlationId,
        CancellationToken ct) =>
        audit?.WriteAsync(action, entityId, oldValue, newValue, correlationId, ct)
        ?? Task.CompletedTask;

    private static void GuardPassword(string password)
    {
        if (string.IsNullOrWhiteSpace(password)
            || password.Length < 10
            || !password.Any(char.IsUpper)
            || !password.Any(char.IsLower)
            || !password.Any(char.IsDigit)
            || password.All(char.IsLetterOrDigit))
        {
            throw new ValidationException("Password must be at least 10 characters and include upper-case, lower-case, number and symbol characters.");
        }
    }
}
