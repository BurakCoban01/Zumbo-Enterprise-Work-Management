using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Infrastructure.Concurrency;
using Zumbo.BuildingBlocks.Infrastructure.Persistence;
using Zumbo.BuildingBlocks.Infrastructure.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Identity;

public sealed record RegisterUserRequest(
    string Username,
    string Email,
    string Password,
    string OrganizationId,
    string? BootstrapToken = null);
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
public sealed record AuthResponse(string AccessToken, string RefreshToken, DateTimeOffset ExpiresAt, UserProfileResponse User);
public sealed record UserProfileResponse(string Id, string Username, string Email, string OrganizationId, IReadOnlyCollection<string> Roles);

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
    public string FrontendResetUrl { get; init; } = "http://localhost:5177/#/reset-password";
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

public sealed class UserDocument : IDocument
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Username { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string OrganizationId { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public string SecurityStamp { get; set; } = Guid.NewGuid().ToString("N");
    public int FailedLoginCount { get; set; }
    public DateTimeOffset? LockedUntil { get; set; }
    public string? PasswordResetTokenHash { get; set; }
    public DateTimeOffset? PasswordResetTokenExpiresAt { get; set; }
    public bool MfaEnabled { get; set; }
    public string? MfaSecretProtected { get; set; }
    public string? PendingMfaSecretProtected { get; set; }
    public DateTimeOffset? PendingMfaExpiresAt { get; set; }
    public List<string> MfaRecoveryCodeHashes { get; set; } = [];
    public List<string> Roles { get; set; } = ["User"];
    public List<RefreshTokenDocument> RefreshTokens { get; set; } = [];
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class RefreshTokenDocument
{
    public string TokenHash { get; set; } = string.Empty;
    public string SessionId { get; set; } = Guid.NewGuid().ToString("N");
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public bool IsActive(DateTimeOffset now) => RevokedAt is null && ExpiresAt > now;
}

public interface IUserRepository
{
    Task<UserDocument?> GetByUsernameOrEmailAsync(string usernameOrEmail, CancellationToken ct);
    Task<UserDocument?> GetByIdAsync(string userId, CancellationToken ct);
    Task<UserDocument?> GetByRefreshTokenAsync(string refreshToken, CancellationToken ct);
    Task<UserDocument?> GetByPasswordResetTokenAsync(string token, CancellationToken ct);
    Task<IReadOnlyList<UserProfileResponse>> SearchAsync(string? search, string? organizationId, CancellationToken ct);
    Task AddAsync(UserDocument user, CancellationToken ct);
    Task UpdateAsync(UserDocument user, CancellationToken ct);
}

public sealed class UserRepository(IDocumentRepository<UserDocument> users) : IUserRepository
{
    public Task<UserDocument?> GetByUsernameOrEmailAsync(string usernameOrEmail, CancellationToken ct)
    {
        var normalized = usernameOrEmail.Trim().ToLowerInvariant();
        return users.SelectAsync(x =>
            x.Username.ToLower() == normalized || x.Email.ToLower() == normalized, ct);
    }

    public Task<UserDocument?> GetByRefreshTokenAsync(string refreshToken, CancellationToken ct)
    {
        var tokenHash = RefreshTokenSecurity.Hash(refreshToken);
        return users.SelectAsync(x => x.RefreshTokens.Any(token => token.TokenHash == tokenHash), ct);
    }

    public Task<UserDocument?> GetByPasswordResetTokenAsync(string token, CancellationToken ct)
    {
        var tokenHash = RefreshTokenSecurity.Hash(token);
        return users.SelectAsync(x => x.PasswordResetTokenHash == tokenHash, ct);
    }

    public Task<UserDocument?> GetByIdAsync(string userId, CancellationToken ct) =>
        users.SelectAsync(x => x.Id == userId, ct);

    public async Task<IReadOnlyList<UserProfileResponse>> SearchAsync(
        string? search,
        string? organizationId,
        CancellationToken ct)
    {
        var normalized = search?.Trim().ToLowerInvariant();
        var result = await users.ListByFilterAsync(
            x => x.IsActive
                && (string.IsNullOrEmpty(organizationId) || x.OrganizationId == organizationId)
                && (string.IsNullOrEmpty(normalized)
                    || x.Username.ToLower().Contains(normalized)
                    || x.Email.ToLower().Contains(normalized)),
            x => x.Username,
            pageSize: 100,
            cancellationToken: ct);

        return result.Select(IdentityMappings.ToProfile).ToList();
    }

    public async Task AddAsync(UserDocument user, CancellationToken ct)
    {
        await users.CreateAsync(user, ct);
    }

    public async Task UpdateAsync(UserDocument user, CancellationToken ct)
    {
        await users.ReplaceByFilterAsync(x => x.Id == user.Id, user, ct);
    }
}

public sealed class IdentityService(
    IUserRepository users,
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
    ICurrentUser currentUser)
{
    public async Task<AuthResponse> RegisterAsync(RegisterUserRequest request, CancellationToken ct)
    {
        GuardRegister(request);

        await using var registrationLock = await AcquireRegistrationLockAsync(ct);

        if (await users.GetByUsernameOrEmailAsync(request.Username, ct) is not null
            || await users.GetByUsernameOrEmailAsync(request.Email, ct) is not null)
        {
            throw new ConflictException("USER_ALREADY_EXISTS", "Username or email is already used.");
        }

        var now = clock.UtcNow;
        var roles = ResolveRegistrationRoles(request);
        var user = new UserDocument
        {
            Username = request.Username.Trim(),
            Email = request.Email.Trim().ToLowerInvariant(),
            OrganizationId = request.OrganizationId.Trim(),
            PasswordHash = passwordHasher.Hash(request.Password),
            SecurityStamp = Guid.NewGuid().ToString("N"),
            CreatedAt = now,
            Roles = roles
        };

        var response = IssueTokens(user, now);
        await users.AddAsync(user, ct);
        return response;
    }

    public async Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.UsernameOrEmail) || string.IsNullOrWhiteSpace(request.Password))
        {
            throw new ValidationException("Username/email and password are required.");
        }

        var candidate = await users.GetByUsernameOrEmailAsync(request.UsernameOrEmail, ct)
            ?? throw new UnauthorizedException("Username or password is invalid.");
        await using var userLock = await AcquireUserLockAsync(candidate.Id, ct);
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
        var response = IssueTokens(user, now);
        await users.UpdateAsync(user, ct);
        return response;
    }

    public async Task<AuthResponse> RefreshAsync(RefreshTokenRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.RefreshToken))
        {
            throw new ValidationException("Refresh token is required.");
        }

        var now = clock.UtcNow;
        var user = await users.GetByRefreshTokenAsync(request.RefreshToken, ct)
            ?? throw new UnauthorizedException("Refresh token is invalid.");
        var tokenHash = RefreshTokenSecurity.Hash(request.RefreshToken);
        var oldToken = user.RefreshTokens.SingleOrDefault(x => x.TokenHash == tokenHash);

        if (!user.IsActive)
        {
            throw new ForbiddenException("User account is inactive.");
        }

        if (oldToken is null)
        {
            throw new UnauthorizedException("Refresh token is invalid.");
        }

        if (oldToken.RevokedAt is not null)
        {
            RevokeAllSessions(user, now);
            user.SecurityStamp = Guid.NewGuid().ToString("N");
            await users.UpdateAsync(user, ct);
            throw new UnauthorizedException("Refresh token reuse was detected.");
        }

        if (oldToken.ExpiresAt <= now)
        {
            throw new UnauthorizedException("Refresh token is expired.");
        }

        oldToken.RevokedAt = now;
        var response = IssueTokens(user, now);
        await users.UpdateAsync(user, ct);
        return response;
    }

    public async Task<LogoutResponse> LogoutAsync(LogoutRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.RefreshToken))
        {
            throw new ValidationException("Refresh token is required.");
        }

        var user = await users.GetByRefreshTokenAsync(request.RefreshToken, ct);
        if (user is null)
        {
            return new LogoutResponse(true, 0);
        }

        var now = clock.UtcNow;
        var tokenHash = RefreshTokenSecurity.Hash(request.RefreshToken);
        var target = user.RefreshTokens.SingleOrDefault(x => x.TokenHash == tokenHash);
        var canRevokeAll = request.AllSessions && target?.IsActive(now) == true;
        var revokedSessions = canRevokeAll
            ? RevokeAllSessions(user, now)
            : RevokeSession(target, now);

        if (canRevokeAll)
        {
            user.SecurityStamp = Guid.NewGuid().ToString("N");
        }

        await users.UpdateAsync(user, ct);
        return new LogoutResponse(true, revokedSessions);
    }

    public async Task<AuthResponse> ChangePasswordAsync(ChangePasswordRequest request, CancellationToken ct)
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
        user.PasswordHash = passwordHasher.Hash(request.NewPassword);
        user.SecurityStamp = Guid.NewGuid().ToString("N");
        RevokeAllSessions(user, now);
        var response = IssueTokens(user, now);
        await users.UpdateAsync(user, ct);
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
        await using (await AcquireUserLockAsync(candidate.Id, ct))
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

    public async Task<PasswordResetResponse> ResetPasswordAsync(ResetPasswordRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Token) || request.Token.Length > 256)
        {
            throw new UnauthorizedException("Password reset token is invalid or expired.");
        }

        GuardPassword(request.NewPassword);
        var candidate = await users.GetByPasswordResetTokenAsync(request.Token, ct)
            ?? throw new UnauthorizedException("Password reset token is invalid or expired.");
        await using var userLock = await AcquireUserLockAsync(candidate.Id, ct);
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
        RevokeAllSessions(user, now);
        await users.UpdateAsync(user, ct);
        return new PasswordResetResponse(true);
    }

    public async Task<BeginMfaSetupResponse> BeginMfaSetupAsync(BeginMfaSetupRequest request, CancellationToken ct)
    {
        var user = await GetCurrentUserAsync(ct);
        await using var userLock = await AcquireUserLockAsync(user.Id, ct);
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
        var issuer = Uri.EscapeDataString("Zumbo");
        var account = Uri.EscapeDataString(user.Email);
        var provisioningUri = $"otpauth://totp/{issuer}:{account}?secret={secret}&issuer={issuer}&digits=6&period=30";
        return new BeginMfaSetupResponse(secret, provisioningUri, expiresAt);
    }

    public async Task<ConfirmMfaSetupResponse> ConfirmMfaSetupAsync(
        ConfirmMfaSetupRequest request,
        CancellationToken ct)
    {
        var user = await GetCurrentUserAsync(ct);
        await using var userLock = await AcquireUserLockAsync(user.Id, ct);
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
        RevokeAllSessions(user, clock.UtcNow);
        await users.UpdateAsync(user, ct);
        return new ConfirmMfaSetupResponse(true, recoveryCodes);
    }

    public async Task<MfaStatusResponse> DisableMfaAsync(DisableMfaRequest request, CancellationToken ct)
    {
        var user = await GetCurrentUserAsync(ct);
        await using var userLock = await AcquireUserLockAsync(user.Id, ct);
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
        RevokeAllSessions(user, clock.UtcNow);
        await users.UpdateAsync(user, ct);
        return new MfaStatusResponse(false, 0);
    }

    public async Task<MfaStatusResponse> GetMfaStatusAsync(CancellationToken ct)
    {
        var user = await GetCurrentUserAsync(ct);
        return new MfaStatusResponse(user.MfaEnabled, user.MfaRecoveryCodeHashes.Count);
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
        RevokeAllSessions(user, now);
        await users.UpdateAsync(user, ct);
        return new AccountStatusResponse(user.Id, false);
    }

    public Task<IReadOnlyList<UserProfileResponse>> SearchUsersAsync(string? search, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(currentUser.UserId))
        {
            throw new UnauthorizedException("Authenticated user is required.");
        }

        var organizationId = currentUser.Roles.Contains("SystemAdmin", StringComparer.OrdinalIgnoreCase)
            ? null
            : currentUser.OrganizationId
                ?? throw new ForbiddenException("Organization scope is required.");
        return users.SearchAsync(search, organizationId, ct);
    }

    private AuthResponse IssueTokens(UserDocument user, DateTimeOffset now)
    {
        var options = jwtOptions.Value;
        user.RefreshTokens.RemoveAll(x => x.ExpiresAt < now.AddDays(-30));
        var rawRefreshToken = tokenIssuer.CreateRefreshToken();
        var refreshToken = new RefreshTokenDocument
        {
            TokenHash = RefreshTokenSecurity.Hash(rawRefreshToken),
            SessionId = Guid.NewGuid().ToString("N"),
            CreatedAt = now,
            ExpiresAt = now.AddDays(14)
        };
        var accessToken = tokenIssuer.CreateAccessToken(
            new TokenUser(
                user.Id,
                user.Username,
                user.Email,
                user.OrganizationId,
                user.Roles,
                user.SecurityStamp,
                refreshToken.SessionId),
            options,
            now);

        user.RefreshTokens.Add(refreshToken);

        return new AuthResponse(
            accessToken,
            rawRefreshToken,
            now.AddMinutes(options.AccessTokenMinutes),
            IdentityMappings.ToProfile(user));
    }

    private static void GuardRegister(RegisterUserRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Username) || request.Username.Length < 3)
        {
            throw new ValidationException("Username must be at least 3 characters.");
        }

        if (string.IsNullOrWhiteSpace(request.Email) || !request.Email.Contains('@'))
        {
            throw new ValidationException("Valid email is required.");
        }

        if (string.IsNullOrWhiteSpace(request.OrganizationId))
        {
            throw new ValidationException("Organization id is required.");
        }

        GuardPassword(request.Password);
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

    private static int RevokeAllSessions(UserDocument user, DateTimeOffset now)
    {
        var active = user.RefreshTokens.Where(x => x.IsActive(now)).ToList();
        active.ForEach(x => x.RevokedAt = now);
        return active.Count;
    }

    private static int RevokeSession(RefreshTokenDocument? token, DateTimeOffset now)
    {
        if (token is null || !token.IsActive(now))
        {
            return 0;
        }

        token.RevokedAt = now;
        return 1;
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

    private async Task<IAsyncDisposable> AcquireUserLockAsync(string userId, CancellationToken ct)
    {
        var options = distributedLockOptions.Value;
        var leaseTime = TimeSpan.FromSeconds(Math.Clamp(options.LeaseSeconds, 5, 300));
        var waitTime = TimeSpan.FromSeconds(Math.Clamp(options.WaitSeconds, 0, 30));
        return await distributedLockProvider.TryAcquireAsync("identity-user:" + userId, leaseTime, waitTime, ct)
            ?? throw new ConflictException("IDENTITY_RESOURCE_BUSY", "The user account is busy; retry the operation.");
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

    private List<string> ResolveRegistrationRoles(RegisterUserRequest request)
    {
        var options = bootstrapOptions.Value;
        var isBootstrapEmail = options.AdminEmails.Any(x =>
            x.Equals(request.Email.Trim(), StringComparison.OrdinalIgnoreCase));
        if (!isBootstrapEmail)
        {
            return ["User"];
        }

        if (string.IsNullOrWhiteSpace(options.BootstrapToken)
            || string.IsNullOrWhiteSpace(request.BootstrapToken)
            || !CryptographicOperations.FixedTimeEquals(
                SHA256.HashData(Encoding.UTF8.GetBytes(options.BootstrapToken)),
                SHA256.HashData(Encoding.UTF8.GetBytes(request.BootstrapToken))))
        {
            throw new ForbiddenException("A valid bootstrap token is required for the configured administrator account.");
        }

        return ["User", "SystemAdmin"];
    }
}

public static class RefreshTokenSecurity
{
    public static string Hash(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}

public static class TotpSecurity
{
    private const string Base32Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    public static string GenerateSecret() => Base32Encode(RandomNumberGenerator.GetBytes(20));

    public static string GenerateCode(string secret, DateTimeOffset now)
    {
        var key = Base32Decode(secret);
        var counter = now.ToUnixTimeSeconds() / 30;
        Span<byte> counterBytes = stackalloc byte[8];
        System.Buffers.Binary.BinaryPrimitives.WriteInt64BigEndian(counterBytes, counter);
        var hash = HMACSHA1.HashData(key, counterBytes);
        var offset = hash[^1] & 0x0F;
        var binary = ((hash[offset] & 0x7F) << 24)
            | (hash[offset + 1] << 16)
            | (hash[offset + 2] << 8)
            | hash[offset + 3];
        return (binary % 1_000_000).ToString("D6");
    }

    public static bool Verify(string secret, string? code, DateTimeOffset now)
    {
        var normalized = code?.Trim();
        if (normalized?.Length != 6 || normalized.Any(x => !char.IsAsciiDigit(x)))
        {
            return false;
        }

        var supplied = Encoding.ASCII.GetBytes(normalized);
        var valid = false;
        for (var offset = -1; offset <= 1; offset++)
        {
            var expected = Encoding.ASCII.GetBytes(GenerateCode(secret, now.AddSeconds(offset * 30)));
            valid |= CryptographicOperations.FixedTimeEquals(supplied, expected);
        }

        return valid;
    }

    public static string GenerateRecoveryCode()
    {
        var raw = Base32Encode(RandomNumberGenerator.GetBytes(8));
        return $"{raw[..4]}-{raw[4..8]}-{raw[8..]}";
    }

    public static string HashRecoveryCode(string code)
    {
        var normalized = new string((code ?? string.Empty)
            .Where(char.IsAsciiLetterOrDigit)
            .Select(char.ToUpperInvariant)
            .ToArray());
        return Convert.ToHexString(SHA256.HashData(Encoding.ASCII.GetBytes(normalized)));
    }

    private static string Base32Encode(ReadOnlySpan<byte> data)
    {
        var output = new StringBuilder((data.Length * 8 + 4) / 5);
        var buffer = 0;
        var bitsLeft = 0;
        foreach (var value in data)
        {
            buffer = (buffer << 8) | value;
            bitsLeft += 8;
            while (bitsLeft >= 5)
            {
                output.Append(Base32Alphabet[(buffer >> (bitsLeft - 5)) & 31]);
                bitsLeft -= 5;
            }
        }

        if (bitsLeft > 0)
        {
            output.Append(Base32Alphabet[(buffer << (5 - bitsLeft)) & 31]);
        }

        return output.ToString();
    }

    private static byte[] Base32Decode(string value)
    {
        var normalized = new string(value
            .Where(char.IsAsciiLetterOrDigit)
            .Select(char.ToUpperInvariant)
            .ToArray());
        var output = new List<byte>(normalized.Length * 5 / 8);
        var buffer = 0;
        var bitsLeft = 0;
        foreach (var character in normalized)
        {
            var index = Base32Alphabet.IndexOf(character);
            if (index < 0)
            {
                throw new CryptographicException("TOTP secret is invalid.");
            }

            buffer = (buffer << 5) | index;
            bitsLeft += 5;
            if (bitsLeft >= 8)
            {
                output.Add((byte)(buffer >> (bitsLeft - 8)));
                bitsLeft -= 8;
            }
        }

        return output.ToArray();
    }
}

public static class IdentityMappings
{
    public static UserProfileResponse ToProfile(this UserDocument user) =>
        new(user.Id, user.Username, user.Email, user.OrganizationId, user.Roles);
}
