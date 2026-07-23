using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Identity;

public sealed record LoginRequest(string UsernameOrEmail, string Password, string? MfaCode = null);
public sealed record RefreshTokenRequest(string RefreshToken);
public sealed record LogoutRequest(string RefreshToken, bool AllSessions = false);
public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);
public sealed record ForgotPasswordRequest(string Email);
public sealed record ResetPasswordRequest(string Token, string NewPassword);
public sealed record PasswordResetRequestedResponse(bool Accepted);
public sealed record PasswordResetResponse(bool Reset);
public sealed record DeactivateAccountRequest(string Password);
public sealed record LogoutResponse(bool LoggedOut, int RevokedSessions);
public sealed record AccountStatusResponse(string UserId, bool IsActive);
public sealed record BeginMfaSetupRequest(string Password);
public sealed record BeginMfaSetupResponse(string Secret, string ProvisioningUri, DateTimeOffset ExpiresAt);
public sealed record ConfirmMfaSetupRequest(string Code);
public sealed record ConfirmMfaSetupResponse(bool Enabled, IReadOnlyCollection<string> RecoveryCodes);
public sealed record DisableMfaRequest(string Password, string Code);
public sealed record MfaStatusResponse(bool Enabled, int RemainingRecoveryCodes);
public sealed record RegenerateMfaRecoveryCodesRequest(string Password, string Code);
public sealed record RegenerateMfaRecoveryCodesResponse(IReadOnlyCollection<string> RecoveryCodes);
public sealed record SessionClientInfo(string DeviceName, string ClientFingerprint);
public sealed record SessionResponse(
    string Id,
    string DeviceName,
    string ClientFingerprint,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastSeenAt,
    DateTimeOffset ExpiresAt,
    DateTimeOffset? RevokedAt,
    bool IsCurrent);

public interface ISessionClientContext
{
    SessionClientInfo GetClientInfo();
}

public sealed class LoginSecurityOptions
{
    public int MaxFailedAttempts { get; init; } = 5;
    public int LockoutMinutes { get; init; } = 15;
}

public sealed class IdentityBootstrapOptions
{
    public IReadOnlyCollection<string> AdminEmails { get; init; } = [];
    public string? BootstrapToken { get; init; }
}

public sealed class PasswordResetOptions
{
    public int ExpiryMinutes { get; init; } = 30;
    public string FrontendResetUrl { get; init; } = string.Empty;
}

public interface IPasswordResetNotifier
{
    Task SendAsync(string email, string rawToken, DateTimeOffset expiresAt, CancellationToken ct);
}

public interface IMfaSecretProtector
{
    string Protect(string secret);
    string Unprotect(string protectedSecret);
}

public sealed partial class IdentityService(
    IUserRepository users,
    IRefreshSessionStore sessions,
    IDurableTransactionRunner transactions,
    IPasswordHasher passwordHasher,
    ITokenIssuer tokenIssuer,
    IOptions<JwtOptions> jwtOptions,
    IOptions<LoginSecurityOptions> loginSecurityOptions,
    IOptions<IdentityBootstrapOptions> bootstrapOptions,
    IOptions<PasswordResetOptions> passwordResetOptions,
    IPasswordResetNotifier passwordResetNotifier,
    IMfaSecretProtector mfaSecretProtector,
    IDistributedLockProvider distributedLockProvider,
    IOptions<DistributedLockOptions> distributedLockOptions,
    IClock clock,
    ICurrentUser currentUser,
    IRegistrationProvisioningPolicy? registrationProvisioningPolicy = null,
    ISessionClientContext? sessionClientContext = null,
    IIdentityAuditWriter? audit = null)
{
    public async Task<AuthResponse> RegisterAsync(RegisterUserRequest request, CancellationToken ct)
    {
        RegisterUserValidator.Validate(request);

        await using var registrationLock = await AcquireRegistrationLockAsync(ct);

        var isBootstrap = ValidateBootstrapRequest(request);
        await (registrationProvisioningPolicy ?? LocalDemoRegistrationProvisioningPolicy.Instance)
            .EnsureAllowedAsync(
                new RegistrationProvisioningRequest(
                    request.Email.Trim().ToLowerInvariant(),
                    request.OrganizationId.Trim().ToLowerInvariant(),
                    isBootstrap),
                ct);

        if (isBootstrap && await users.HasSystemAdminAsync(ct))
        {
            throw new ConflictException(
                "BOOTSTRAP_ALREADY_COMPLETED",
                "System administrator bootstrap has already been completed.");
        }

        if (await users.GetByUsernameOrEmailAsync(request.Username, ct) is not null
            || await users.GetByUsernameOrEmailAsync(request.Email, ct) is not null)
        {
            throw new ConflictException("USER_ALREADY_EXISTS", "Username or email is already used.");
        }

        var now = clock.UtcNow;
        var roles = isBootstrap ? new List<string> { "User", "SystemAdmin" } : ["User"];
        var user = new UserDocument
        {
            Username = request.Username.Trim(),
            Email = request.Email.Trim().ToLowerInvariant(),
            OrganizationId = request.OrganizationId.Trim().ToLowerInvariant(),
            PasswordHash = passwordHasher.Hash(request.Password),
            SecurityStamp = Guid.NewGuid().ToString("N"),
            CreatedAt = now,
            Roles = roles
        };

        return await transactions.ExecuteAsync(
            "Identity",
            async token =>
            {
                await users.AddAsync(user, token);
                return await IssueTokensAsync(user, now, token);
            },
            ct);
    }

    public async Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken ct)
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

    public async Task<AuthResponse> RefreshAsync(RefreshTokenRequest request, CancellationToken ct)
    {
        DocumentConcurrencyException? lastConflict = null;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                var result = await transactions.ExecuteAsync(
                    "Identity",
                    token => RefreshAttemptAsync(request, token),
                    ct);
                if (result.ReuseDetected)
                {
                    throw new UnauthorizedException("Refresh token reuse was detected.");
                }

                return result.Response!;
            }
            catch (DocumentConcurrencyException conflict)
            {
                lastConflict = conflict;
            }
        }

        throw lastConflict!;
    }

    private async Task<RefreshAttemptResult> RefreshAttemptAsync(
        RefreshTokenRequest request,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.RefreshToken))
        {
            throw new ValidationException("Refresh token is required.");
        }

        var now = clock.UtcNow;
        var oldSession = await GetOrImportRefreshSessionAsync(request.RefreshToken, ct)
            ?? throw new UnauthorizedException("Refresh token is invalid.");
        var user = await users.GetByIdAsync(oldSession.UserId, ct)
            ?? throw new UnauthorizedException("Refresh token is invalid.");

        if (!user.IsActive || user.OrganizationId != oldSession.OrganizationId)
        {
            throw new ForbiddenException("User account is inactive.");
        }

        if (oldSession.RevokedAt is not null)
        {
            await sessions.RevokeAllAsync(user.Id, user.OrganizationId, now, ct);
            LegacyRefreshSessionCompatibility.RevokeAll(user, now);
            user.SecurityStamp = Guid.NewGuid().ToString("N");
            await users.UpdateAsync(user, ct);
            return RefreshAttemptResult.Reused;
        }

        if (oldSession.ExpiresAt <= now)
        {
            throw new UnauthorizedException("Refresh token is expired.");
        }

        var replacement = CreateTokenResponse(user, now, oldSession);
        if (!await sessions.RevokeAsync(oldSession, now, replacement.Session.Id, ct))
        {
            throw new UnauthorizedException("Refresh token is no longer active.");
        }

        await sessions.CreateAsync(replacement.Session, ct);
        return new RefreshAttemptResult(replacement.Response, false);
    }

    public async Task<LogoutResponse> LogoutAsync(LogoutRequest request, CancellationToken ct)
    {
        DocumentConcurrencyException? lastConflict = null;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                return await transactions.ExecuteAsync(
                    "Identity",
                    token => LogoutAttemptAsync(request, token),
                    ct);
            }
            catch (DocumentConcurrencyException conflict)
            {
                lastConflict = conflict;
            }
        }

        throw lastConflict!;
    }

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

    public async Task<PasswordResetRequestedResponse> ForgotPasswordAsync(
        ForgotPasswordRequest request,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Email) || !request.Email.Contains('@') || request.Email.Length > 254)
        {
            throw new ValidationException("Valid email is required.");
        }

        var candidate = await users.GetByUsernameOrEmailAsync(request.Email, ct);
        if (candidate is null || !candidate.IsActive
            || !candidate.Email.Equals(request.Email.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            RefreshTokenSecurity.Hash(Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
            return new PasswordResetRequestedResponse(true);
        }

        string email;
        string rawToken;
        DateTimeOffset expiresAt;
        {
            var user = await users.GetByIdAsync(candidate.Id, ct);
            if (user is null || !user.IsActive)
            {
                return new PasswordResetRequestedResponse(true);
            }

            rawToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48))
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');
            expiresAt = clock.UtcNow.AddMinutes(Math.Clamp(passwordResetOptions.Value.ExpiryMinutes, 5, 120));
            user.PasswordResetTokenHash = RefreshTokenSecurity.Hash(rawToken);
            user.PasswordResetTokenExpiresAt = expiresAt;
            email = user.Email;
            await users.UpdateAsync(user, ct);
        }

        await passwordResetNotifier.SendAsync(email, rawToken, expiresAt, ct);
        return new PasswordResetRequestedResponse(true);
    }

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

    public Task<IReadOnlyList<SessionResponse>> ListSessionsAsync(CancellationToken ct) =>
        ListSessionsAsync(null, ct);

    public async Task<IReadOnlyList<SessionResponse>> ListSessionsAsync(string? currentSessionId, CancellationToken ct)
    {
        var user = await GetCurrentUserAsync(ct);
        var ownedSessions = await sessions.ListOwnedAsync(user.Id, user.OrganizationId, ct);
        return ownedSessions
            .Select(session => new SessionResponse(
                session.Id,
                session.DeviceName,
                session.ClientFingerprint,
                session.CreatedAt,
                session.LastSeenAt == default ? session.CreatedAt : session.LastSeenAt,
                session.ExpiresAt,
                session.RevokedAt,
                string.Equals(session.Id, currentSessionId, StringComparison.Ordinal)))
            .ToList();
    }

    public async Task RevokeSessionAsync(string sessionId, string correlationId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(sessionId) || sessionId.Length > 64)
        {
            throw new NotFoundException("SESSION_NOT_FOUND", "Session was not found.");
        }

        var user = await GetCurrentUserAsync(ct);
        DocumentConcurrencyException? lastConflict = null;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var session = await sessions.GetByIdAsync(sessionId, user.Id, user.OrganizationId, ct)
                ?? throw new NotFoundException("SESSION_NOT_FOUND", "Session was not found.");
            if (session.RevokedAt is not null)
            {
                return;
            }

            try
            {
                if (!await sessions.RevokeAsync(session, clock.UtcNow, null, ct))
                {
                    continue;
                }

                await WriteAuditAsync("SessionRevoked", user.Id, session.Id, null, correlationId, ct);
                return;
            }
            catch (DocumentConcurrencyException conflict)
            {
                lastConflict = conflict;
            }
        }

        if (lastConflict is not null)
        {
            throw lastConflict;
        }

        throw new ConflictException("SESSION_REVOKE_CONFLICT", "Session could not be revoked; retry the operation.");
    }

    public async Task<AccountStatusResponse> DeactivateAsync(DeactivateAccountRequest request, CancellationToken ct)
    {
        var user = await GetCurrentUserAsync(ct);
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

    public Task<IReadOnlyList<UserProfileResponse>> SearchUsersAsync(string? search, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(currentUser.UserId))
        {
            throw new UnauthorizedException("Authenticated user is required.");
        }

        var organizationId = PermissionCatalog.IsSystemAdministrator(currentUser.Roles)
            ? null
            : currentUser.OrganizationId
                ?? throw new ForbiddenException("Organization scope is required.");
        return users.SearchAsync(search, organizationId, ct);
    }

    private async Task<AuthResponse> IssueTokensAsync(
        UserDocument user,
        DateTimeOffset now,
        CancellationToken ct)
    {
        _ = await sessions.PurgeRetainedAsync(now, 100, ct);
        var created = CreateTokenResponse(user, now);
        await sessions.CreateAsync(created.Session, ct);
        return created.Response;
    }

    private async Task<RefreshSessionDocument?> GetOrImportRefreshSessionAsync(
        string rawToken,
        CancellationToken ct)
    {
        var stored = await sessions.GetByTokenAsync(rawToken, ct);
        if (stored is not null)
        {
            return stored;
        }

        var legacyUser = await users.GetByRefreshTokenAsync(rawToken, ct);
        var tokenHash = RefreshTokenSecurity.Hash(rawToken);
        var legacy = legacyUser?.RefreshTokens.SingleOrDefault(x => x.TokenHash == tokenHash);
        if (legacyUser is null || legacy is null)
        {
            return null;
        }

        var imported = new RefreshSessionDocument
        {
            Id = legacy.SessionId,
            UserId = legacyUser.Id,
            OrganizationId = legacyUser.OrganizationId,
            TokenHash = legacy.TokenHash,
            CreatedAt = legacy.CreatedAt,
            LastSeenAt = legacy.CreatedAt,
            DeviceName = "Legacy session",
            ExpiresAt = legacy.ExpiresAt,
            ExpiresAtUtc = legacy.ExpiresAt.UtcDateTime,
            RevokedAt = legacy.RevokedAt,
            RetainUntilUtc = (legacy.RevokedAt ?? legacy.ExpiresAt).AddDays(30).UtcDateTime
        };
        try
        {
            await sessions.CreateAsync(imported, ct);
            return imported;
        }
        catch (DocumentConflictException)
        {
            return await sessions.GetByTokenAsync(rawToken, ct);
        }
    }

    private TokenResponseWithSession CreateTokenResponse(
        UserDocument user,
        DateTimeOffset now,
        RefreshSessionDocument? previousSession = null)
    {
        var options = jwtOptions.Value;
        var rawRefreshToken = tokenIssuer.CreateRefreshToken();
        var client = GetSessionClientInfo(previousSession);
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

    private async Task<UserDocument> GetCurrentUserAsync(CancellationToken ct)
    {
        var userId = currentUser.UserId;
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new UnauthorizedException("Authenticated user is required.");
        }

        return await users.GetByIdAsync(userId, ct)
            ?? throw new UnauthorizedException("Authenticated user was not found.");
    }

    private SessionClientInfo GetSessionClientInfo(RefreshSessionDocument? previousSession)
    {
        var supplied = sessionClientContext?.GetClientInfo();
        var deviceName = NormalizeDeviceName(supplied?.DeviceName)
            ?? NormalizeDeviceName(previousSession?.DeviceName)
            ?? "Unknown client";
        var fingerprint = supplied?.ClientFingerprint?.Trim();
        if (string.IsNullOrWhiteSpace(fingerprint) || fingerprint.Length > 128)
        {
            fingerprint = previousSession?.ClientFingerprint ?? string.Empty;
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

    private async Task<int> RevokeSessionAsync(
        RefreshSessionDocument? session,
        DateTimeOffset now,
        CancellationToken ct)
    {
        if (session is null || !session.IsActive(now))
        {
            return 0;
        }

        return await sessions.RevokeAsync(session, now, null, ct) ? 1 : 0;
    }

    private sealed record TokenResponseWithSession(
        AuthResponse Response,
        RefreshSessionDocument Session);

    private sealed record RefreshAttemptResult(AuthResponse? Response, bool ReuseDetected)
    {
        public static RefreshAttemptResult Reused { get; } = new(null, true);
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

    private Task<IAsyncDisposable> AcquireRegistrationLockAsync(CancellationToken ct) =>
        AcquireLockAsync("identity-registration", "IDENTITY_REGISTRATION_BUSY", ct);

    private async Task<IAsyncDisposable> AcquireLockAsync(string resource, string errorCode, CancellationToken ct)
    {
        var options = distributedLockOptions.Value;
        var leaseTime = TimeSpan.FromSeconds(Math.Clamp(options.LeaseSeconds, 5, 300));
        var waitTime = TimeSpan.FromSeconds(Math.Clamp(options.WaitSeconds, 0, 30));
        return await distributedLockProvider.TryAcquireAsync(resource, leaseTime, waitTime, ct)
            ?? throw new ConflictException(errorCode, "Identity resource is busy; retry the operation.");
    }

    private bool ValidateBootstrapRequest(RegisterUserRequest request)
    {
        var options = bootstrapOptions.Value;
        var isBootstrapEmail = options.AdminEmails.Any(x =>
            x.Equals(request.Email.Trim(), StringComparison.OrdinalIgnoreCase));
        if (!isBootstrapEmail)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(options.BootstrapToken)
            || string.IsNullOrWhiteSpace(request.BootstrapToken)
            || !CryptographicOperations.FixedTimeEquals(
                SHA256.HashData(Encoding.UTF8.GetBytes(options.BootstrapToken)),
                SHA256.HashData(Encoding.UTF8.GetBytes(request.BootstrapToken))))
        {
            throw new ForbiddenException("A valid bootstrap token is required for the configured administrator account.");
        }

        return true;
    }
}
