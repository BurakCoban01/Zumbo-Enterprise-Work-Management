using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Identity.Application.Features.Login;

public sealed class LoginHandler(
    IUserRepository users,
    IRefreshSessionStore sessions,
    IDurableTransactionRunner transactions,
    IPasswordHasher passwordHasher,
    ITokenIssuer tokenIssuer,
    IOptions<JwtOptions> jwtOptions,
    IOptions<LoginSecurityOptions> loginSecurityOptions,
    IMfaSecretProtector mfaSecretProtector,
    IClock clock,
    ISessionClientContext? sessionClientContext = null)
{
    public async Task<AuthResponse> HandleAsync(LoginRequest request, CancellationToken ct)
    {
        DocumentConcurrencyException? lastConflict = null;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                return await LoginAttemptAsync(request, ct);
            }
            catch (DocumentConcurrencyException conflict)
            {
                lastConflict = conflict;
            }
        }

        throw lastConflict!;
    }

    private async Task<AuthResponse> LoginAttemptAsync(LoginRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.UsernameOrEmail) || string.IsNullOrWhiteSpace(request.Password))
        {
            throw new ValidationException("Username/email and password are required.");
        }

        var candidate = await users.GetByUsernameOrEmailAsync(request.UsernameOrEmail, ct)
            ?? throw new UnauthorizedException("Username or password is invalid.");
        var user = await users.GetByIdAsync(candidate.Id, ct)
            ?? throw new UnauthorizedException("Username or password is invalid.");
        var now = clock.UtcNow;

        if (user.LockedUntil > now)
        {
            throw new UnauthorizedException("Username or password is invalid.");
        }

        if (user.LockedUntil.HasValue)
        {
            user.FailedLoginCount = 0;
            user.LockedUntil = null;
        }

        if (!passwordHasher.Verify(request.Password, user.PasswordHash))
        {
            user.FailedLoginCount++;
            var securityOptions = loginSecurityOptions.Value;
            var maxFailedAttempts = Math.Clamp(securityOptions.MaxFailedAttempts, 3, 20);
            if (user.FailedLoginCount >= maxFailedAttempts)
            {
                var lockoutMinutes = Math.Clamp(securityOptions.LockoutMinutes, 1, 1440);
                user.LockedUntil = now.AddMinutes(lockoutMinutes);
            }

            await users.UpdateAsync(user, ct);
            throw new UnauthorizedException("Username or password is invalid.");
        }

        if (!user.IsActive)
        {
            throw new ForbiddenException("User account is inactive.");
        }

        if (passwordHasher.NeedsRehash(user.PasswordHash))
        {
            user.PasswordHash = passwordHasher.Hash(request.Password);
        }

        if (user.MfaEnabled)
        {
            if (string.IsNullOrWhiteSpace(request.MfaCode))
            {
                throw new AuthenticationChallengeException("MFA_REQUIRED", "A multi-factor authentication code is required.");
            }

            if (!ConsumeMfaCode(user, request.MfaCode, now))
            {
                await RecordFailedLoginAsync(user, now, ct);
                throw new AuthenticationChallengeException("MFA_INVALID", "Multi-factor authentication code is invalid.");
            }
        }

        user.FailedLoginCount = 0;
        user.LockedUntil = null;
        return await transactions.ExecuteAsync(
            "Identity",
            async token =>
            {
                await users.UpdateAsync(user, token);
                return await IssueTokensAsync(user, now, token);
            },
            ct);
    }

    private async Task RecordFailedLoginAsync(UserDocument user, DateTimeOffset now, CancellationToken ct)
    {
        user.FailedLoginCount++;
        var securityOptions = loginSecurityOptions.Value;
        if (user.FailedLoginCount >= Math.Clamp(securityOptions.MaxFailedAttempts, 3, 20))
        {
            user.LockedUntil = now.AddMinutes(Math.Clamp(securityOptions.LockoutMinutes, 1, 1440));
        }

        await users.UpdateAsync(user, ct);
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

    private async Task<AuthResponse> IssueTokensAsync(UserDocument user, DateTimeOffset now, CancellationToken ct)
    {
        _ = await sessions.PurgeRetainedAsync(now, 100, ct);
        var created = CreateTokenResponse(user, now);
        await sessions.CreateAsync(created.Session, ct);
        return created.Response;
    }

    private TokenResponseWithSession CreateTokenResponse(UserDocument user, DateTimeOffset now)
    {
        var options = jwtOptions.Value;
        var rawRefreshToken = tokenIssuer.CreateRefreshToken();
        var client = GetSessionClientInfo();
        var refreshSession = new RefreshSessionDocument
        {
            UserId = user.Id,
            OrganizationId = user.OrganizationId,
            TokenHash = RefreshTokenSecurity.Hash(rawRefreshToken),
            CreatedAt = now,
            LastSeenAt = now,
            DeviceName = client.DeviceName,
            ClientFingerprint = client.ClientFingerprint,
            ExpiresAt = now.AddDays(14),
            ExpiresAtUtc = now.AddDays(14).UtcDateTime,
            RetainUntilUtc = now.AddDays(44).UtcDateTime
        };
        var accessToken = tokenIssuer.CreateAccessToken(
            new TokenUser(
                user.Id,
                user.Username,
                user.Email,
                user.OrganizationId,
                user.Roles,
                user.SecurityStamp,
                refreshSession.Id),
            options,
            now);

        return new TokenResponseWithSession(
            new AuthResponse(
                accessToken,
                rawRefreshToken,
                now.AddMinutes(options.AccessTokenMinutes),
                IdentityMappings.ToProfile(user)),
            refreshSession);
    }

    private SessionClientInfo GetSessionClientInfo()
    {
        var supplied = sessionClientContext?.GetClientInfo();
        var deviceName = NormalizeDeviceName(supplied?.DeviceName) ?? "Unknown client";
        var fingerprint = supplied?.ClientFingerprint?.Trim();
        if (string.IsNullOrWhiteSpace(fingerprint) || fingerprint.Length > 128)
        {
            fingerprint = string.Empty;
        }

        return new SessionClientInfo(deviceName, fingerprint);
    }

    private static string? NormalizeDeviceName(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrWhiteSpace(normalized)
            || normalized.Length > 80
            || normalized.Any(char.IsControl)
                ? null
                : normalized;
    }

    private sealed record TokenResponseWithSession(AuthResponse Response, RefreshSessionDocument Session);
}
