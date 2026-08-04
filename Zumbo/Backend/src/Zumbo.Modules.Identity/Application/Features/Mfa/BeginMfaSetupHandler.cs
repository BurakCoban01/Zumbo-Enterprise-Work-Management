using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Identity.Application.Features.Mfa;

public sealed class BeginMfaSetupHandler(
    IUserRepository users,
    IPasswordHasher passwordHasher,
    IMfaSecretProtector mfaSecretProtector,
    IClock clock,
    ICurrentUser currentUser,
    IIdentityAuditWriter? audit = null)
{
    public async Task<BeginMfaSetupResponse> HandleAsync(
        BeginMfaSetupRequest request,
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
