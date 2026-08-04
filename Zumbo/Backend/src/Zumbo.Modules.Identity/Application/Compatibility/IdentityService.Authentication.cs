using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;
using Zumbo.Modules.Identity.Application.Features.Login;
using Zumbo.Modules.Identity.Application.Features.Logout;

namespace Zumbo.Modules.Identity;

public sealed partial class IdentityService
{
    public async Task<AuthResponse> RegisterAsync(RegisterUserRequest request, CancellationToken ct) =>
            await registerUserHandler.HandleAsync(request, ct);

    public async Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken ct) =>
            await loginHandler.HandleAsync(request, ct);

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

    public async Task<LogoutResponse> LogoutAsync(LogoutRequest request, CancellationToken ct) =>
            await logoutHandler.HandleAsync(request, ct);

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

    private async Task<IAsyncDisposable> AcquireLockAsync(string resource, string errorCode, CancellationToken ct)
        {
            var options = distributedLockOptions.Value;
            var leaseTime = TimeSpan.FromSeconds(Math.Clamp(options.LeaseSeconds, 5, 300));
            var waitTime = TimeSpan.FromSeconds(Math.Clamp(options.WaitSeconds, 0, 30));
            return await distributedLockProvider.TryAcquireAsync(resource, leaseTime, waitTime, ct)
                ?? throw new ConflictException(errorCode, "Identity resource is busy; retry the operation.");
        }

    private Task<IAsyncDisposable> AcquireRegistrationLockAsync(CancellationToken ct) =>
            AcquireLockAsync("identity-registration", "IDENTITY_REGISTRATION_BUSY", ct);

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
