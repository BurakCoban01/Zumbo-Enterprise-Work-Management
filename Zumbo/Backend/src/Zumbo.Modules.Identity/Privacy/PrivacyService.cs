using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Identity;

public sealed class PrivacyService(
    IUserRepository users,
    IRefreshSessionStore sessions,
    IApiKeyStore apiKeys,
    IDurableTransactionRunner transactions,
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

    public async Task<long> StreamExportAsync(Stream destination, CancellationToken ct)
    {
        var user = await GetCurrentUserAsync(ct);
        return await dataProcessor.WriteExportAsync(
            user.Id,
            user.OrganizationId,
            user.ToProfile(),
            destination,
            ct);
    }

    public async Task<AnonymizeAccountResponse> AnonymizeAsync(
        AnonymizeAccountRequest request,
        string correlationId,
        CancellationToken ct)
    {
        var context = await ValidateAnonymizationAsync(request, ct);
        await AnonymizeReferencesForWorkflowAsync(context.UserId, context.Pseudonym, ct);
        await FinalizeAnonymizationForWorkflowAsync(
            context.UserId,
            context.Pseudonym,
            correlationId,
            ct);
        return new AnonymizeAccountResponse(true, context.Pseudonym);
    }

    public async Task<PrivacyAnonymizationContext> ValidateAnonymizationAsync(
        AnonymizeAccountRequest request,
        CancellationToken ct)
    {
        var user = await GetCurrentUserAsync(ct);
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
        return new PrivacyAnonymizationContext(
            user.Id,
            user.OrganizationId,
            pseudonym,
            user.Username,
            user.Email);
    }

    public async Task AnonymizeReferencesForWorkflowAsync(
        string userId,
        string pseudonym,
        CancellationToken ct)
    {
        await using var userLock = await AcquireUserLockAsync(userId, ct);
        var user = await users.GetByIdAsync(userId, ct)
            ?? throw new NotFoundException("PRIVACY_USER_NOT_FOUND", "Privacy workflow user was not found.");
        await dataProcessor.EnsureCanAnonymizeAsync(user.Id, user.OrganizationId, ct);
        await dataProcessor.AnonymizeReferencesAsync(
            user.Id,
            user.OrganizationId,
            pseudonym,
            user.Username,
            user.Email,
            ct);
    }

    public async Task FinalizeAnonymizationForWorkflowAsync(
        string userId,
        string pseudonym,
        string correlationId,
        CancellationToken ct)
    {
        await using var userLock = await AcquireUserLockAsync(userId, ct);
        var user = await users.GetByIdAsync(userId, ct)
            ?? throw new NotFoundException("PRIVACY_USER_NOT_FOUND", "Privacy workflow user was not found.");

        await transactions.ExecuteAsync(
            "Identity",
            async token =>
            {
                var keys = await apiKeys.ListAllOwnedAsync(user.Id, user.OrganizationId, token);
                foreach (var key in keys.Where(x => x.RevokedAt is null))
                {
                    key.RevokedAt = clock.UtcNow;
                    key.RevokedAtUtc = key.RevokedAt.Value.UtcDateTime;
                    if (!await apiKeys.ReplaceOwnedAsync(key, token))
                    {
                        throw new DocumentConflictException("API key disappeared during anonymization.");
                    }
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
                LegacyRefreshSessionCompatibility.RevokeAll(user, clock.UtcNow);
                await sessions.RevokeAllAsync(user.Id, user.OrganizationId, clock.UtcNow, token);
                await users.UpdateAsync(user, token);
            },
            ct);
        await audit.WriteAsync(
            "UserAnonymized",
            user.Id,
            "active",
            pseudonym,
            correlationId,
            ct);
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
