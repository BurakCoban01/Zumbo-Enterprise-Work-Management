using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.Modules.Identity;

namespace Zumbo.RepositoryContracts;

public abstract class IdentityCredentialStoreContract
{
    protected abstract IDocumentRepository<RefreshSessionDocument> CreateSessionRepository();
    protected abstract IDocumentRepository<ApiKeyDocument> CreateApiKeyRepository();

    [Fact]
    public async Task RefreshSessions_EnforceOwnershipCasAndBoundedRetention()
    {
        var repository = CreateSessionRepository();
        var store = new RefreshSessionStore(repository);
        var prefix = NewId("session");
        var now = DateTimeOffset.UtcNow;
        var first = Session($"{prefix}-first", "user-a", "org-a", $"{prefix}-raw-a", now.AddDays(1));
        var second = Session($"{prefix}-second", "user-b", "org-b", $"{prefix}-raw-b", now.AddDays(1));

        try
        {
            await store.CreateAsync(first, CancellationToken.None);
            await store.CreateAsync(second, CancellationToken.None);

            Assert.Equal(first.Id, (await store.GetByTokenAsync($"{prefix}-raw-a", CancellationToken.None))?.Id);
            Assert.Null(await store.GetByIdAsync(first.Id, "user-b", "org-b", CancellationToken.None));
            var ownedSessions = await store.ListOwnedAsync("user-a", "org-a", CancellationToken.None);
            Assert.Equal(first.Id, Assert.Single(ownedSessions).Id);
            Assert.Equal(1, await store.RevokeAllAsync("user-a", "org-a", now, CancellationToken.None));
            Assert.NotNull((await store.GetByIdAsync(first.Id, "user-a", "org-a", CancellationToken.None))?.RevokedAt);
            Assert.Null((await store.GetByIdAsync(second.Id, "user-b", "org-b", CancellationToken.None))?.RevokedAt);

            var writers = await Task.WhenAll(
                store.GetByIdAsync(second.Id, "user-b", "org-b", CancellationToken.None),
                store.GetByIdAsync(second.Id, "user-b", "org-b", CancellationToken.None));
            var outcomes = await Task.WhenAll(writers.Select(writer => TryRevokeAsync(store, writer!, now)));
            Assert.Equal(1, outcomes.Count(outcome => outcome == "revoked"));
            Assert.Equal(1, outcomes.Count(outcome => outcome == "conflict"));

            for (var index = 0; index < 3; index++)
            {
                var expired = Session(
                    $"{prefix}-expired-{index}",
                    "purge-user",
                    "purge-org",
                    $"{prefix}-expired-raw-{index}",
                    now.AddDays(-40));
                expired.RetainUntilUtc = now.AddDays(-1).UtcDateTime;
                await store.CreateAsync(expired, CancellationToken.None);
            }

            Assert.Equal(2, await store.PurgeRetainedAsync(now, 2, CancellationToken.None));
            Assert.Equal(1, await repository.CountByFilterAsync(document =>
                document.Id.StartsWith(prefix) && document.UserId == "purge-user"));
        }
        finally
        {
            await repository.DeleteByFilterAsync(document => document.Id.StartsWith(prefix));
        }
    }

    [Fact]
    public async Task ApiKeys_EnforceOwnershipCasAndBoundedExpiryRetention()
    {
        var repository = CreateApiKeyRepository();
        var store = new ApiKeyStore(repository);
        var prefix = NewId("api-key");
        var now = DateTimeOffset.UtcNow;
        var active = ApiKey($"{prefix}-active", "user-a", "org-a", now.AddDays(1));

        try
        {
            await store.CreateAsync(active, CancellationToken.None);
            Assert.Null(await store.GetOwnedAsync(active.Id, "user-a", "other-org", CancellationToken.None));

            var owned = await store.GetOwnedAsync(active.Id, "user-a", "org-a", CancellationToken.None);
            owned!.LastUsedAt = now;
            Assert.True(await store.ReplaceOwnedAsync(owned, CancellationToken.None));
            Assert.Equal(2, owned.Version);

            var stale = await store.GetOwnedAsync(active.Id, "user-a", "org-a", CancellationToken.None);
            var current = await store.GetOwnedAsync(active.Id, "user-a", "org-a", CancellationToken.None);
            current!.LastUsedAt = now.AddSeconds(1);
            Assert.True(await store.ReplaceOwnedAsync(current, CancellationToken.None));
            stale!.RevokedAt = now;
            await Assert.ThrowsAsync<DocumentConcurrencyException>(
                () => store.ReplaceOwnedAsync(stale, CancellationToken.None));

            for (var index = 0; index < 3; index++)
            {
                await store.CreateAsync(ApiKey(
                    $"{prefix}-expired-{index}",
                    "purge-user",
                    "purge-org",
                    now.AddDays(-1)), CancellationToken.None);
            }

            Assert.Equal(2, await store.PurgeExpiredAsync(now, 2, CancellationToken.None));
            Assert.Equal(1, await repository.CountByFilterAsync(document =>
                document.Id.StartsWith(prefix) && document.UserId == "purge-user"));
        }
        finally
        {
            await repository.DeleteByFilterAsync(document => document.Id.StartsWith(prefix));
        }
    }

    private static RefreshSessionDocument Session(
        string id,
        string userId,
        string organizationId,
        string rawToken,
        DateTimeOffset expiresAt) =>
        new()
        {
            Id = id,
            UserId = userId,
            OrganizationId = organizationId,
            TokenHash = RefreshTokenSecurity.Hash(rawToken),
            CreatedAt = DateTimeOffset.UtcNow,
            LastSeenAt = DateTimeOffset.UtcNow,
            DeviceName = "Contract client",
            ClientFingerprint = "CONTRACT-FINGERPRINT",
            ExpiresAt = expiresAt,
            ExpiresAtUtc = expiresAt.UtcDateTime,
            RetainUntilUtc = expiresAt.AddDays(30).UtcDateTime
        };

    private static ApiKeyDocument ApiKey(
        string id,
        string userId,
        string organizationId,
        DateTimeOffset expiresAt) =>
        new()
        {
            Id = id,
            UserId = userId,
            OrganizationId = organizationId,
            Name = id,
            KeyPrefix = "zmb_contract",
            KeyHash = new string('a', 64),
            Scopes = ["api:full"],
            CreatedAt = DateTimeOffset.UtcNow,
            ExpiresAt = expiresAt,
            ExpiresAtUtc = expiresAt.UtcDateTime
        };

    private static async Task<string> TryRevokeAsync(
        RefreshSessionStore store,
        RefreshSessionDocument session,
        DateTimeOffset now)
    {
        try
        {
            return await store.RevokeAsync(session, now, null, CancellationToken.None)
                ? "revoked"
                : "missing";
        }
        catch (DocumentConcurrencyException)
        {
            return "conflict";
        }
    }

    private static string NewId(string scenario) =>
        $"data006-{scenario}-{Guid.NewGuid():N}";
}
