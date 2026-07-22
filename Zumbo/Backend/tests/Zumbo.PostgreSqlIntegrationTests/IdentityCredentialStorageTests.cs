using System.Data.Common;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.Modules.Identity;

namespace Zumbo.PostgreSqlIntegrationTests;

[Collection(PostgreSqlCollection.Name)]
public sealed class IdentityCredentialStorageTests(PostgreSqlFixture fixture)
{
    [Fact]
    public async Task CredentialIndexes_AreDeclaredOnSeparateOwnedStores()
    {
        await using var connection = await fixture.Api.OpenConnectionAsync(CancellationToken.None);
        Assert.Equal(7L, await PostgreSqlFixture.ScalarAsync<long>(connection, """
            SELECT count(*)
            FROM pg_catalog.pg_indexes
            WHERE schemaname = 'identity'
              AND indexname IN (
                  'ix_refresh_sessions_document_gin',
                  'ux_refresh_sessions_token_hash',
                  'ix_refresh_sessions_owner_active',
                  'ix_refresh_sessions_retain_until',
                  'ix_api_keys_owner_created',
                  'ix_api_keys_owner_revoked_expires',
                  'ix_api_keys_expires_utc');
            """));
        Assert.Equal(0L, await PostgreSqlFixture.ScalarAsync<long>(connection, """
            SELECT count(*)
            FROM pg_catalog.pg_indexes
            WHERE schemaname = 'identity'
              AND indexname = 'ix_users_refresh_token_hash';
            """));
    }

    [Fact]
    public async Task SeparateStores_EnforceOwnershipCasAndBoundedRetention()
    {
        var repository = fixture.Api.CreateRepository<RefreshSessionDocument>("identity", "refresh_sessions");
        var store = new RefreshSessionStore(repository);
        var prefix = Guid.NewGuid().ToString("N");
        var now = DateTimeOffset.UtcNow;
        var firstRaw = $"raw-{prefix}-first";
        var secondRaw = $"raw-{prefix}-second";
        var first = Session($"{prefix}-first", "user-a", "org-a", firstRaw, now.AddDays(1));
        var second = Session($"{prefix}-second", "user-b", "org-b", secondRaw, now.AddDays(1));

        try
        {
            await store.CreateAsync(first, CancellationToken.None);
            await store.CreateAsync(second, CancellationToken.None);

            Assert.Equal(first.Id, (await store.GetByTokenAsync(firstRaw, CancellationToken.None))?.Id);
            Assert.Null(await store.GetByIdAsync(first.Id, "user-b", "org-b", CancellationToken.None));
            Assert.Equal(1, await store.RevokeAllAsync("user-a", "org-a", now, CancellationToken.None));
            Assert.NotNull((await store.GetByIdAsync(first.Id, "user-a", "org-a", CancellationToken.None))?.RevokedAt);
            Assert.Null((await store.GetByIdAsync(second.Id, "user-b", "org-b", CancellationToken.None))?.RevokedAt);

            var staleA = await store.GetByIdAsync(second.Id, "user-b", "org-b", CancellationToken.None);
            var staleB = await store.GetByIdAsync(second.Id, "user-b", "org-b", CancellationToken.None);
            var outcomes = await Task.WhenAll(
                TryRevokeAsync(store, staleA!, now),
                TryRevokeAsync(store, staleB!, now));
            Assert.Equal(1, outcomes.Count(x => x == "revoked"));
            Assert.Equal(1, outcomes.Count(x => x == "conflict"));

            for (var index = 0; index < 3; index++)
            {
                var expired = Session(
                    $"{prefix}-expired-{index}",
                    "purge-user",
                    "purge-org",
                    $"raw-{prefix}-expired-{index}",
                    now.AddDays(-40));
                expired.RetainUntilUtc = now.AddDays(-1).UtcDateTime;
                await store.CreateAsync(expired, CancellationToken.None);
            }

            Assert.Equal(2, await store.PurgeRetainedAsync(now, 2, CancellationToken.None));
            Assert.Equal(1, await repository.CountByFilterAsync(x =>
                x.UserId == "purge-user" && x.OrganizationId == "purge-org"));
        }
        finally
        {
            await repository.DeleteByFilterAsync(x => x.Id.StartsWith(prefix));
        }
    }

    [Fact]
    public async Task ApiKeyStore_IsTenantOwnedAndVersioned()
    {
        var repository = fixture.Api.CreateRepository<ApiKeyDocument>("identity", "api_keys");
        var store = new ApiKeyStore(repository);
        var id = Guid.NewGuid().ToString("N");
        var key = new ApiKeyDocument
        {
            Id = id,
            UserId = "api-user",
            OrganizationId = "api-org",
            Name = "provider test",
            KeyPrefix = "zmb_provider",
            KeyHash = new string('a', 64),
            Scopes = ["api:full"],
            CreatedAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(1),
            ExpiresAtUtc = DateTime.UtcNow.AddDays(1)
        };

        try
        {
            await store.CreateAsync(key, CancellationToken.None);
            Assert.Null(await store.GetOwnedAsync(id, "api-user", "other-org", CancellationToken.None));
            var owned = await store.GetOwnedAsync(id, "api-user", "api-org", CancellationToken.None);
            owned!.LastUsedAt = DateTimeOffset.UtcNow;
            Assert.True(await store.ReplaceOwnedAsync(owned, CancellationToken.None));
            Assert.Equal(2, owned.Version);
            Assert.Single(await store.ListOwnedAsync("api-user", "api-org", CancellationToken.None));
        }
        finally
        {
            await repository.DeleteByFilterAsync(x => x.Id == id);
        }
    }

    [Fact]
    public async Task CredentialMigration_BackfillsLegacySessionAndCanRollbackReapply()
    {
        var migrations = await fixture.Api.GetAppliedMigrationsAsync(CancellationToken.None);
        var credentialMigration = Assert.Single(
            migrations,
            migration => migration.StartsWith("7:create_identity_credential_stores", StringComparison.Ordinal));
        var suffix = Guid.NewGuid().ToString("N");
        var userId = $"legacy-user-{suffix}";
        var sessionId = $"legacy-session-{suffix}";
        var runtimeSessionId = $"runtime-session-{suffix}";
        var apiKeyId = $"legacy-api-key-{suffix}";

        await fixture.Api.RollbackAsync(credentialMigration, CancellationToken.None);
        try
        {
            await using var connection = await fixture.Api.OpenConnectionAsync(CancellationToken.None);
            await PostgreSqlFixture.ExecuteAsync(connection, $"""
                INSERT INTO identity.users (id, version, document)
                VALUES (
                    '{userId}',
                    1,
                    jsonb_build_object(
                        'Id', '{userId}',
                        'Version', 1,
                        'OrganizationId', 'legacy-org',
                        'RefreshTokens', jsonb_build_array(jsonb_build_object(
                            'SessionId', '{sessionId}',
                            'TokenHash', 'legacy-hash-{suffix}',
                            'CreatedAt', '2029-12-01T00:00:00+00:00',
                            'ExpiresAt', '2030-01-01T00:00:00+00:00',
                            'RevokedAt', null))));
                INSERT INTO identity.api_keys (id, version, document)
                VALUES (
                    '{apiKeyId}',
                    0,
                    jsonb_build_object(
                        'Id', '{apiKeyId}',
                        'UserId', '{userId}',
                        'OrganizationId', 'legacy-org',
                        'ExpiresAt', '2030-01-01T00:00:00+00:00',
                        'RevokedAt', null));
                """);

            await fixture.Api.MigrateAsync(CancellationToken.None);
            Assert.Equal(1L, await PostgreSqlFixture.ScalarAsync<long>(connection, $"""
                SELECT count(*)
                FROM identity.refresh_sessions
                WHERE id = '{sessionId}'
                  AND document ->> 'UserId' = '{userId}'
                  AND document ->> 'OrganizationId' = 'legacy-org'
                  AND document ->> 'TokenHash' = 'legacy-hash-{suffix}';
                """));
            Assert.Equal(1L, await PostgreSqlFixture.ScalarAsync<long>(connection, $"""
                SELECT jsonb_array_length(document -> 'RefreshTokens')
                FROM identity.users
                WHERE id = '{userId}';
                """));
            Assert.Equal(1L, await PostgreSqlFixture.ScalarAsync<long>(connection, $"""
                SELECT count(*)
                FROM identity.api_keys
                WHERE id = '{apiKeyId}'
                  AND version = 1
                  AND (document ->> 'Version')::bigint = 1
                  AND document ->> 'ExpiresAtUtc' = '2030-01-01T00:00:00+00:00'
                  AND document ? 'RevokedAtUtc';
                """));

            var sessionRepository = fixture.Api.CreateRepository<RefreshSessionDocument>(
                "identity",
                "refresh_sessions");
            var sessionStore = new RefreshSessionStore(sessionRepository);
            var imported = await sessionStore.GetByIdAsync(
                sessionId,
                userId,
                "legacy-org",
                CancellationToken.None);
            Assert.Equal(1, imported!.Version);
            Assert.True(await sessionStore.RevokeAsync(
                imported,
                DateTimeOffset.UtcNow,
                null,
                CancellationToken.None));
            await sessionStore.CreateAsync(
                Session(
                    runtimeSessionId,
                    userId,
                    "legacy-org",
                    "runtime-raw-" + suffix,
                    DateTimeOffset.UtcNow.AddDays(1)),
                CancellationToken.None);

            await fixture.Api.RollbackAsync(credentialMigration, CancellationToken.None);
            await fixture.Api.MigrateAsync(CancellationToken.None);
            Assert.Equal(2L, await PostgreSqlFixture.ScalarAsync<long>(connection, $"""
                SELECT count(*)
                FROM identity.refresh_sessions
                WHERE id IN ('{sessionId}', '{runtimeSessionId}');
                """));
        }
        finally
        {
            await fixture.Api.MigrateAsync(CancellationToken.None);
            await using var cleanup = await fixture.Api.OpenConnectionAsync(CancellationToken.None);
            await PostgreSqlFixture.ExecuteAsync(cleanup, $"""
                DELETE FROM identity.refresh_sessions WHERE id = '{sessionId}';
                DELETE FROM identity.refresh_sessions WHERE id = '{runtimeSessionId}';
                DELETE FROM identity.api_keys WHERE id = '{apiKeyId}';
                DELETE FROM identity.users WHERE id = '{userId}';
                """);
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
            ExpiresAt = expiresAt,
            ExpiresAtUtc = expiresAt.UtcDateTime,
            RetainUntilUtc = expiresAt.AddDays(30).UtcDateTime
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
}
