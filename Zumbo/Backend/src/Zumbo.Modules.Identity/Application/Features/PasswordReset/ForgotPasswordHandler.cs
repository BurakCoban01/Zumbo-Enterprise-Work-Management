using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Identity.Application.Features.PasswordReset;

public sealed class ForgotPasswordHandler(
    IUserRepository users,
    IOptions<PasswordResetOptions> passwordResetOptions,
    IPasswordResetNotifier passwordResetNotifier,
    IClock clock)
{
    public async Task<PasswordResetRequestedResponse> HandleAsync(
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
}
