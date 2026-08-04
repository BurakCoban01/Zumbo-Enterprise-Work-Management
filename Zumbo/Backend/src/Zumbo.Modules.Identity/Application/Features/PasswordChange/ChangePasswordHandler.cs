using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Identity.Application.Features.PasswordChange;

public sealed class ChangePasswordHandler(
    IUserRepository users,
    IRefreshSessionStore sessions,
    IDurableTransactionRunner transactions,
    IPasswordHasher passwordHasher,
    ITokenIssuer tokenIssuer,
    IOptions<JwtOptions> jwtOptions,
    IClock clock,
    ICurrentUser currentUser,
    IIdentityAuditWriter? audit = null,
    ISessionClientContext? sessionClientContext = null)
{
    public async Task<AuthResponse> HandleAsync(
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

    private sealed record TokenResponseWithSession(
        AuthResponse Response,
        RefreshSessionDocument Session);
}
