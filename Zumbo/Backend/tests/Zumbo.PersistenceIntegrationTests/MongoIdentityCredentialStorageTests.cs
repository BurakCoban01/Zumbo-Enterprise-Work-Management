using Microsoft.Extensions.Configuration;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Infrastructure.Persistence;
using Zumbo.Modules.Identity;

namespace Zumbo.PersistenceIntegrationTests;

public sealed class MongoIdentityCredentialStorageTests : IAsyncLifetime
{
    private readonly MongoDbService mongo;
    private readonly MongoTransactionContext context = new();
    private readonly MongoRepository<RefreshSessionDocument> sessionDocuments;
    private readonly MongoRepository<ApiKeyDocument> apiKeyDocuments;
    private readonly RefreshSessionStore sessions;
    private readonly ApiKeyStore apiKeys;
    private readonly MongoDurableTransactionRunner transactions;
    private readonly string databaseName;

    public MongoIdentityCredentialStorageTests()
    {
        var connectionString = Environment.GetEnvironmentVariable("ZUMBO_TEST_MONGO_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "ZUMBO_TEST_MONGO_CONNECTION_STRING is required for real Mongo identity credential tests.");
        }

        databaseName = $"ZumboData006_{Guid.NewGuid():N}";
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["MongoDb:ConnectionString"] = connectionString,
                ["MongoDb:DatabaseName"] = databaseName,
                ["Modules:Identity:MongoDb:DatabaseName"] = databaseName
            })
            .Build();
        mongo = new MongoDbService(configuration);
        sessionDocuments = new MongoRepository<RefreshSessionDocument>(mongo, context);
        apiKeyDocuments = new MongoRepository<ApiKeyDocument>(mongo, context);
        sessions = new RefreshSessionStore(sessionDocuments);
        apiKeys = new ApiKeyStore(apiKeyDocuments);
        transactions = new MongoDurableTransactionRunner(mongo, context);
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        await context.DisposeAsync();
        await mongo.GetDatabase("Identity").Client.DropDatabaseAsync(databaseName);
    }

    [Fact]
    public async Task SeparateCredentialStores_AreAtomicOwnedAndCompareExchangeProtected()
    {
        var now = DateTimeOffset.UtcNow;
        var rollbackSession = Session("rollback-session", "rollback-user", "org-a", "rollback-raw", now);
        var rollbackKey = ApiKey("rollback-key", "rollback-user", "org-a", now);

        await Assert.ThrowsAsync<IntentionalFailure>(() => transactions.ExecuteAsync(
            "Identity",
            async token =>
            {
                await sessions.CreateAsync(rollbackSession, token);
                await apiKeys.CreateAsync(rollbackKey, token);
                throw new IntentionalFailure();
            },
            CancellationToken.None));
        Assert.Null(await sessions.GetByIdAsync(
            rollbackSession.Id,
            rollbackSession.UserId,
            rollbackSession.OrganizationId,
            CancellationToken.None));
        Assert.Null(await apiKeys.GetByIdAsync(rollbackKey.Id, CancellationToken.None));

        var first = Session("session-a", "user-a", "org-a", "raw-a", now);
        var second = Session("session-b", "user-b", "org-b", "raw-b", now);
        var key = ApiKey("key-a", "user-a", "org-a", now);
        await transactions.ExecuteAsync(
            "Identity",
            async token =>
            {
                await sessions.CreateAsync(first, token);
                await sessions.CreateAsync(second, token);
                await apiKeys.CreateAsync(key, token);
            },
            CancellationToken.None);

        Assert.Null(await sessions.GetByIdAsync(first.Id, "user-b", "org-b", CancellationToken.None));
        Assert.Null(await apiKeys.GetOwnedAsync(key.Id, "user-a", "org-b", CancellationToken.None));
        Assert.Equal(1, await sessions.RevokeAllAsync("user-a", "org-a", now, CancellationToken.None));
        Assert.NotNull((await sessions.GetByIdAsync(first.Id, "user-a", "org-a", CancellationToken.None))?.RevokedAt);
        Assert.Null((await sessions.GetByIdAsync(second.Id, "user-b", "org-b", CancellationToken.None))?.RevokedAt);

        var staleA = await sessions.GetByIdAsync(second.Id, "user-b", "org-b", CancellationToken.None);
        var staleB = await sessions.GetByIdAsync(second.Id, "user-b", "org-b", CancellationToken.None);
        var outcomes = await Task.WhenAll(
            TryRevokeAsync(staleA!, now),
            TryRevokeAsync(staleB!, now));
        Assert.Equal(1, outcomes.Count(x => x == "revoked"));
        Assert.Equal(1, outcomes.Count(x => x == "conflict"));

        var ownedKey = await apiKeys.GetOwnedAsync(key.Id, "user-a", "org-a", CancellationToken.None);
        ownedKey!.LastUsedAt = now;
        Assert.True(await apiKeys.ReplaceOwnedAsync(ownedKey, CancellationToken.None));
        Assert.Equal(2, ownedKey.Version);
    }

    private async Task<string> TryRevokeAsync(RefreshSessionDocument session, DateTimeOffset now)
    {
        try
        {
            return await sessions.RevokeAsync(session, now, null, CancellationToken.None)
                ? "revoked"
                : "missing";
        }
        catch (DocumentConcurrencyException)
        {
            return "conflict";
        }
    }

    private static RefreshSessionDocument Session(
        string id,
        string userId,
        string organizationId,
        string rawToken,
        DateTimeOffset now) =>
        new()
        {
            Id = id,
            UserId = userId,
            OrganizationId = organizationId,
            TokenHash = RefreshTokenSecurity.Hash(rawToken),
            CreatedAt = now,
            ExpiresAt = now.AddDays(14),
            ExpiresAtUtc = now.AddDays(14).UtcDateTime,
            RetainUntilUtc = now.AddDays(44).UtcDateTime
        };

    private static ApiKeyDocument ApiKey(
        string id,
        string userId,
        string organizationId,
        DateTimeOffset now) =>
        new()
        {
            Id = id,
            UserId = userId,
            OrganizationId = organizationId,
            Name = id,
            KeyPrefix = "zmb_test",
            KeyHash = new string('a', 64),
            Scopes = ["api:full"],
            CreatedAt = now,
            ExpiresAt = now.AddDays(30),
            ExpiresAtUtc = now.AddDays(30).UtcDateTime
        };

    private sealed class IntentionalFailure : Exception;
}
