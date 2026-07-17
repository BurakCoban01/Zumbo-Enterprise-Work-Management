using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Identity;

public sealed class UserDocument : IVersionedDocument
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
    public long Version { get; set; }
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
    Task<bool> HasSystemAdminAsync(CancellationToken ct);
    Task<IReadOnlyList<UserProfileResponse>> SearchAsync(string? search, string? organizationId, CancellationToken ct);
    Task AddAsync(UserDocument user, CancellationToken ct);
    Task UpdateAsync(UserDocument user, CancellationToken ct);
}

public sealed class UserRepository(
    IDocumentRepository<UserDocument> users,
    IExpectedVersionAccessor? expectedVersions = null) : IUserRepository
{
    private readonly ExpectedVersionState expectedVersion = new(expectedVersions);

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

    public Task<bool> HasSystemAdminAsync(CancellationToken ct) =>
        users.ExistsByFilterAsync(x => x.Roles.Contains("SystemAdmin"), ct);

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
        var result = await users.ReplaceByVersionAsync(
            x => x.Id == user.Id,
            user,
            expectedVersion.Consume(user.Version),
            ct);
        if (!result.Found)
        {
            throw new NotFoundException("USER_NOT_FOUND", "User was not found.");
        }

        user.Version = result.Version!.Value;
    }
}
