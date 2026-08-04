using System.Security.Cryptography;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Identity;

public sealed partial class IdentityService
{
    public Task<BeginMfaSetupResponse> BeginMfaSetupAsync(BeginMfaSetupRequest request, CancellationToken ct) =>
        BeginMfaSetupAsync(request, "system", ct);

    public async Task<BeginMfaSetupResponse> BeginMfaSetupAsync(
        BeginMfaSetupRequest request,
        string correlationId,
        CancellationToken ct)
    {
        var user = await GetCurrentUserAsync(ct);
        user = await users.GetByIdAsync(user.Id, ct)
            ?? throw new UnauthorizedException("Authenticated user was not found.");
        if (user.MfaEnabled)
        {
            throw new ConflictException("MFA_ALREADY_ENABLED", "Multi-factor authentication is already enabled.");
        }

        if (string.IsNullOrWhiteSpace(request.Password)
            || !passwordHasher.Verify(request.Password, user.PasswordHash))
        {
            throw new UnauthorizedException("Password is invalid.");
        }

        var secret = TotpSecurity.GenerateSecret();
        var expiresAt = clock.UtcNow.AddMinutes(10);
        user.PendingMfaSecretProtected = mfaSecretProtector.Protect(secret);
        user.PendingMfaExpiresAt = expiresAt;
        await users.UpdateAsync(user, ct);
        await WriteAuditAsync("MfaSetupStarted", user.Id, null, null, correlationId, ct);
        var issuer = Uri.EscapeDataString("Zumbo");
        var account = Uri.EscapeDataString(user.Email);
        var provisioningUri = $"otpauth://totp/{issuer}:{account}?secret={secret}&issuer={issuer}&digits=6&period=30";
        return new BeginMfaSetupResponse(secret, provisioningUri, expiresAt);
    }

    public Task<ConfirmMfaSetupResponse> ConfirmMfaSetupAsync(
        ConfirmMfaSetupRequest request,
        CancellationToken ct) =>
        ConfirmMfaSetupAsync(request, "system", ct);

    public async Task<ConfirmMfaSetupResponse> ConfirmMfaSetupAsync(
        ConfirmMfaSetupRequest request,
        string correlationId,
        CancellationToken ct)
    {
        var user = await GetCurrentUserAsync(ct);
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

    public Task<MfaStatusResponse> DisableMfaAsync(DisableMfaRequest request, CancellationToken ct) =>
        DisableMfaAsync(request, "system", ct);

    public async Task<MfaStatusResponse> DisableMfaAsync(
        DisableMfaRequest request,
        string correlationId,
        CancellationToken ct)
    {
        var user = await GetCurrentUserAsync(ct);
        user = await users.GetByIdAsync(user.Id, ct)
            ?? throw new UnauthorizedException("Authenticated user was not found.");
        if (!user.MfaEnabled)
        {
            return new MfaStatusResponse(false, 0);
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

        user.MfaEnabled = false;
        user.MfaSecretProtected = null;
        user.PendingMfaSecretProtected = null;
        user.PendingMfaExpiresAt = null;
        user.MfaRecoveryCodeHashes.Clear();
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
        await WriteAuditAsync("MfaDisabled", user.Id, null, null, correlationId, ct);
        return new MfaStatusResponse(false, 0);
    }

    public async Task<RegenerateMfaRecoveryCodesResponse> RegenerateMfaRecoveryCodesAsync(
        RegenerateMfaRecoveryCodesRequest request,
        string correlationId,
        CancellationToken ct)
    {
        var user = await GetCurrentUserAsync(ct);
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

    public async Task<MfaStatusResponse> GetMfaStatusAsync(CancellationToken ct)
    {
        var user = await GetCurrentUserAsync(ct);
        return new MfaStatusResponse(user.MfaEnabled, user.MfaRecoveryCodeHashes.Count);
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
}
