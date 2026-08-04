using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Identity;

internal sealed class RegisterUserSlice(
    IUserRepository users,
    IRefreshSessionStore sessions,
    IDurableTransactionRunner transactions,
    IPasswordHasher passwordHasher,
    ITokenIssuer tokenIssuer,
    IOptions<JwtOptions> jwtOptions,
    IOptions<IdentityBootstrapOptions> bootstrapOptions,
    IDistributedLockProvider distributedLockProvider,
    IOptions<DistributedLockOptions> distributedLockOptions,
    IClock clock,
    IRegistrationProvisioningPolicy? registrationProvisioningPolicy,
    ISessionClientContext? sessionClientContext)
{
    internal async Task<AuthResponse> HandleAsync(RegisterUserRequest request, CancellationToken ct)
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

    private async Task<IAsyncDisposable> AcquireRegistrationLockAsync(CancellationToken ct)
    {
        var options = distributedLockOptions.Value;
        var leaseTime = TimeSpan.FromSeconds(Math.Clamp(options.LeaseSeconds, 5, 300));
        var waitTime = TimeSpan.FromSeconds(Math.Clamp(options.WaitSeconds, 0, 30));
        return await distributedLockProvider.TryAcquireAsync(
                "identity-registration",
                leaseTime,
                waitTime,
                ct)
            ?? throw new ConflictException(
                "IDENTITY_REGISTRATION_BUSY",
                "Identity resource is busy; retry the operation.");
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

    private RegistrationTokenResult CreateTokenResponse(UserDocument user, DateTimeOffset now)
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

        return new RegistrationTokenResult(
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
}
