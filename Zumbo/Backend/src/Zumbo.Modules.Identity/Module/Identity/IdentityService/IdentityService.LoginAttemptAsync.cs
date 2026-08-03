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
}
