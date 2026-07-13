using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Infrastructure.Concurrency;
using Zumbo.BuildingBlocks.Infrastructure.Persistence;
using Zumbo.BuildingBlocks.Infrastructure.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Identity;

public sealed record PrivacyDataReference(string ResourceId, string Detail);
public sealed record PrivacyDataGroup(
    string Category,
    IReadOnlyCollection<PrivacyDataReference> Items,
    bool Truncated);
public sealed record PrivacyExportResponse(
    UserProfileResponse Profile,
    DateTimeOffset GeneratedAt,
    IReadOnlyCollection<PrivacyDataGroup> Data);
public sealed record AnonymizeAccountRequest(string Password, string Confirmation);
public sealed record AnonymizeAccountResponse(bool Anonymized, string Pseudonym);

public interface IPrivacyDataProcessor
{
    Task<IReadOnlyCollection<PrivacyDataGroup>> ExportAsync(
        string userId,
        string organizationId,
        CancellationToken ct);
    Task EnsureCanAnonymizeAsync(string userId, string organizationId, CancellationToken ct);
    Task AnonymizeReferencesAsync(
        string userId,
        string organizationId,
        string pseudonym,
        string username,
        string email,
        CancellationToken ct);
}

public sealed class PrivacyService(
    IUserRepository users,
    IDocumentRepository<ApiKeyDocument> apiKeys,
    IPasswordHasher passwordHasher,
    IPrivacyDataProcessor dataProcessor,
    IIdentityAuditWriter audit,
    IDistributedLockProvider distributedLockProvider,
    IOptions<DistributedLockOptions> distributedLockOptions,
    IClock clock,
    ICurrentUser currentUser)
{
    public async Task<PrivacyExportResponse> ExportAsync(CancellationToken ct)
    {
        var user = await GetCurrentUserAsync(ct);
        var data = await dataProcessor.ExportAsync(user.Id, user.OrganizationId, ct);
        return new PrivacyExportResponse(user.ToProfile(), clock.UtcNow, data);
    }

    public async Task<AnonymizeAccountResponse> AnonymizeAsync(
        AnonymizeAccountRequest request,
        string correlationId,
        CancellationToken ct)
    {
        var candidate = await GetCurrentUserAsync(ct);
        await using var userLock = await AcquireUserLockAsync(candidate.Id, ct);
        var user = await users.GetByIdAsync(candidate.Id, ct)
            ?? throw new UnauthorizedException("Authenticated user was not found.");
        if (request.Confirmation != "ANONYMIZE")
        {
            throw new ValidationException("Confirmation must exactly match ANONYMIZE.");
        }

        if (string.IsNullOrWhiteSpace(request.Password)
            || !passwordHasher.Verify(request.Password, user.PasswordHash))
        {
            throw new UnauthorizedException("Password is invalid.");
        }

        await dataProcessor.EnsureCanAnonymizeAsync(user.Id, user.OrganizationId, ct);
        var pseudonym = "anon-" + Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(user.Id)))[..16].ToLowerInvariant();
        await audit.WriteAsync(
            "UserAnonymized",
            user.Id,
            "active",
            pseudonym,
            correlationId,
            ct);
        await dataProcessor.AnonymizeReferencesAsync(
            user.Id,
            user.OrganizationId,
            pseudonym,
            user.Username,
            user.Email,
            ct);

        var keys = await LoadAllApiKeysAsync(user.Id, ct);
        foreach (var key in keys.Where(x => x.RevokedAt is null))
        {
            key.RevokedAt = clock.UtcNow;
            await apiKeys.ReplaceByFilterAsync(x => x.Id == key.Id, key, ct);
        }

        user.Username = pseudonym;
        user.Email = pseudonym + "@invalid.local";
        user.PasswordHash = passwordHasher.Hash(Convert.ToBase64String(RandomNumberGenerator.GetBytes(48)) + "A1!");
        user.IsActive = false;
        user.SecurityStamp = Guid.NewGuid().ToString("N");
        user.FailedLoginCount = 0;
        user.LockedUntil = null;
        user.PasswordResetTokenHash = null;
        user.PasswordResetTokenExpiresAt = null;
        user.MfaEnabled = false;
        user.MfaSecretProtected = null;
        user.PendingMfaSecretProtected = null;
        user.PendingMfaExpiresAt = null;
        user.MfaRecoveryCodeHashes.Clear();
        user.Roles = ["User"];
        foreach (var token in user.RefreshTokens.Where(x => x.IsActive(clock.UtcNow)))
        {
            token.RevokedAt = clock.UtcNow;
        }

        await users.UpdateAsync(user, ct);
        return new AnonymizeAccountResponse(true, pseudonym);
    }

    private async Task<IReadOnlyList<ApiKeyDocument>> LoadAllApiKeysAsync(string userId, CancellationToken ct)
    {
        var result = new List<ApiKeyDocument>();
        for (var page = 1; ; page++)
        {
            var batch = await apiKeys.ListByFilterAsync(
                x => x.UserId == userId,
                x => x.Id,
                page: page,
                pageSize: 200,
                cancellationToken: ct);
            result.AddRange(batch);
            if (batch.Count < 200)
            {
                return result;
            }
        }
    }

    private async Task<UserDocument> GetCurrentUserAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(currentUser.UserId))
        {
            throw new UnauthorizedException("Authenticated user is required.");
        }

        return await users.GetByIdAsync(currentUser.UserId, ct)
            ?? throw new UnauthorizedException("Authenticated user was not found.");
    }

    private async Task<IAsyncDisposable> AcquireUserLockAsync(string userId, CancellationToken ct)
    {
        var options = distributedLockOptions.Value;
        return await distributedLockProvider.TryAcquireAsync(
            "identity-user:" + userId,
            TimeSpan.FromSeconds(Math.Clamp(options.LeaseSeconds, 5, 300)),
            TimeSpan.FromSeconds(Math.Clamp(options.WaitSeconds, 0, 30)),
            ct)
            ?? throw new ConflictException("IDENTITY_RESOURCE_BUSY", "The user account is busy; retry the operation.");
    }
}
