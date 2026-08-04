using System.Security.Cryptography;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Identity.Application.Features.Mfa;

public sealed class RegenerateMfaRecoveryCodesHandler(
    IUserRepository users,
    IRefreshSessionStore sessions,
    IDurableTransactionRunner transactions,
    IPasswordHasher passwordHasher,
    IMfaSecretProtector mfaSecretProtector,
    IClock clock,
    ICurrentUser currentUser,
    IIdentityAuditWriter? audit = null)
{
    public async Task<RegenerateMfaRecoveryCodesResponse> HandleAsync(
        RegenerateMfaRecoveryCodesRequest request,
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
        if (!user.MfaEnabled || string.IsNullOrWhiteSpace(user.MfaSecretProtected))
        {
            throw new ConflictException("MFA_NOT_ENABLED", "Multi-factor authentication is not enabled.");
        }

        if (string.IsNullOrWhiteSpace(request.Password)
            || !passwordHasher.Verify(request.Password, user.PasswordHash))
        {
            throw new UnauthorizedException("Password is invalid.");
        }

        if (!ConsumeMfaCode(user, request.Code, clock.UtcNow))
        {
            throw new AuthenticationChallengeException("MFA_INVALID", "Multi-factor authentication code is invalid.");
        }

        var recoveryCodes = Enumerable.Range(0, 8).Select(_ => TotpSecurity.GenerateRecoveryCode()).ToList();
        user.MfaRecoveryCodeHashes = recoveryCodes.Select(TotpSecurity.HashRecoveryCode).ToList();
        user.SecurityStamp = Guid.NewGuid().ToString("N");
        var now = clock.UtcNow;
        LegacyRefreshSessionCompatibility.RevokeAll(user, now);
        await transactions.ExecuteAsync(
            "Identity",
            async token =>
            {
                await sessions.RevokeAllAsync(user.Id, user.OrganizationId, now, token);
                await users.UpdateAsync(user, token);
            },
            ct);
        await WriteAuditAsync("MfaRecoveryCodesRegenerated", user.Id, null, null, correlationId, ct);
        return new RegenerateMfaRecoveryCodesResponse(recoveryCodes);
    }

    private bool ConsumeMfaCode(UserDocument user, string code, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(user.MfaSecretProtected))
        {
            return false;
        }

        var secret = mfaSecretProtector.Unprotect(user.MfaSecretProtected);
        if (TotpSecurity.Verify(secret, code, now))
        {
            return true;
        }

        var recoveryHash = TotpSecurity.HashRecoveryCode(code);
        var recoveryIndex = user.MfaRecoveryCodeHashes.FindIndex(x =>
            CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(x),
                Convert.FromHexString(recoveryHash)));
        if (recoveryIndex < 0)
        {
            return false;
        }

        user.MfaRecoveryCodeHashes.RemoveAt(recoveryIndex);
        return true;
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
