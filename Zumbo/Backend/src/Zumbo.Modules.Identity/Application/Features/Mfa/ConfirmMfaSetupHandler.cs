using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Identity.Application.Features.Mfa;

public sealed class ConfirmMfaSetupHandler(
    IUserRepository users,
    IRefreshSessionStore sessions,
    IDurableTransactionRunner transactions,
    IMfaSecretProtector mfaSecretProtector,
    IClock clock,
    ICurrentUser currentUser,
    IIdentityAuditWriter? audit = null)
{
    public async Task<ConfirmMfaSetupResponse> HandleAsync(
        ConfirmMfaSetupRequest request,
        string correlationId,
        CancellationToken ct)
    {
        var userId = currentUser.UserId;
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new UnauthorizedException("Authenticated user is required.");
        }

        var user = await users.GetByIdAsync(userId, ct)
            ?? throw new UnauthorizedException("Authenticated user was not found.");
        user = await users.GetByIdAsync(user.Id, ct)
            ?? throw new UnauthorizedException("Authenticated user was not found.");
        if (user.MfaEnabled)
        {
            throw new ConflictException("MFA_ALREADY_ENABLED", "Multi-factor authentication is already enabled.");
        }

        if (user.PendingMfaExpiresAt <= clock.UtcNow || string.IsNullOrWhiteSpace(user.PendingMfaSecretProtected))
        {
            user.PendingMfaSecretProtected = null;
            user.PendingMfaExpiresAt = null;
            await users.UpdateAsync(user, ct);
            throw new AuthenticationChallengeException("MFA_SETUP_EXPIRED", "Multi-factor authentication setup has expired.");
        }

        var secret = mfaSecretProtector.Unprotect(user.PendingMfaSecretProtected);
        if (!TotpSecurity.Verify(secret, request.Code, clock.UtcNow))
        {
            throw new AuthenticationChallengeException("MFA_INVALID", "Multi-factor authentication code is invalid.");
        }

        var recoveryCodes = Enumerable.Range(0, 8).Select(_ => TotpSecurity.GenerateRecoveryCode()).ToList();
        user.MfaEnabled = true;
        user.MfaSecretProtected = user.PendingMfaSecretProtected;
        user.PendingMfaSecretProtected = null;
        user.PendingMfaExpiresAt = null;
        user.MfaRecoveryCodeHashes = recoveryCodes.Select(TotpSecurity.HashRecoveryCode).ToList();
        user.SecurityStamp = Guid.NewGuid().ToString("N");
        LegacyRefreshSessionCompatibility.RevokeAll(user, clock.UtcNow);
        await transactions.ExecuteAsync(
            "Identity",
            async token =>
            {
                await sessions.RevokeAllAsync(user.Id, user.OrganizationId, clock.UtcNow, token);
                await users.UpdateAsync(user, token);
            },
            ct);
        await WriteAuditAsync("MfaEnabled", user.Id, null, null, correlationId, ct);
        return new ConfirmMfaSetupResponse(true, recoveryCodes);
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
}
