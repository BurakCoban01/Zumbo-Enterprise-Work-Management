using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using Zumbo.BuildingBlocks.Infrastructure.Persistence;

public sealed partial class MongoMigrationRunner{

    private async Task<MongoMigrationOutcome> ApplyIndexesAsync(
        string migrationId,
        IReadOnlyList<MongoIndexSpecification> indexes,
        CancellationToken cancellationToken)
    {
        var checksum = Checksum(string.Join('|', indexes.Select(SerializeIndex)));
        var existing = await LoadLedgerAsync(migrationId, cancellationToken);
        if (existing is not null)
        {
            EnsureChecksum(existing, checksum);
            if (existing.State == MongoMigrationStates.Completed)
            {
                return ToOutcome(existing, MongoMigrationStates.Skipped);
            }
        }

        if (_options.DryRun)
        {
            return new MongoMigrationOutcome(migrationId, MongoMigrationStates.DryRun, indexes.Count);
        }

        var ledger = await GetOrCreateLedgerAsync(migrationId, checksum, cancellationToken);
        ledger = await AcquireLeaseAsync(ledger, cancellationToken);
        if (ledger.LeaseOwner != _owner)
        {
            return ToOutcome(ledger, MongoMigrationStates.Busy);
        }

        var indexesToCreate = indexes.Where(index => !IsSupersededIndex(migrationId, index.Name));
        var createdIndexes = 0;
        foreach (var group in indexesToCreate.GroupBy(x => (x.Module, x.Collection)))
        {
            var collection = mongo.GetCollection<BsonDocument>(group.Key.Collection, group.Key.Module);
            using var cursor = await collection.Indexes.ListAsync(cancellationToken);
            var currentIndexes = await cursor.ToListAsync(cancellationToken);
            var models = group
                .Where(specification => !currentIndexes.Any(index =>
                    IsEquivalentIndex(index, specification)))
                .Select(specification => new CreateIndexModel<BsonDocument>(
                specification.Keys,
                new CreateIndexOptions<BsonDocument>
                {
                    Name = specification.Name,
                    Unique = specification.Unique,
                    Collation = specification.CaseInsensitive
                        ? new Collation("en", strength: CollationStrength.Secondary)
                        : null,
                    ExpireAfter = specification.ExpireAfter,
                    PartialFilterExpression = specification.PartialFilter
                })).ToList();
            if (models.Count > 0)
            {
                await collection.Indexes.CreateManyAsync(models, cancellationToken);
                createdIndexes += models.Count;
            }
        }

        ledger.Examined = indexes.Count;
        ledger.Changed = createdIndexes;
        ledger.State = MongoMigrationStates.Completed;
        ledger.CompletedAt = DateTime.UtcNow;
        await SaveAndReleaseOwnedLedgerAsync(ledger, cancellationToken);
        return ToOutcome(ledger, MongoMigrationStates.Completed);
    }

    private static bool IsEquivalentIndex(
        BsonDocument current,
        MongoIndexSpecification expected)
    {
        if (current["key"].AsBsonDocument != expected.Keys
            || current.GetValue("unique", false).ToBoolean() != expected.Unique)
        {
            return false;
        }

        var currentCaseInsensitive = current.TryGetValue("collation", out var collation)
            && collation.AsBsonDocument.GetValue("locale", string.Empty).AsString == "en"
            && collation.AsBsonDocument.GetValue("strength", 0).ToInt32() == 2;
        if (currentCaseInsensitive != expected.CaseInsensitive)
        {
            return false;
        }

        var currentExpiry = current.TryGetValue("expireAfterSeconds", out var expiry)
            ? TimeSpan.FromSeconds(expiry.ToInt64())
            : (TimeSpan?)null;
        if (currentExpiry != expected.ExpireAfter)
        {
            return false;
        }

        var currentPartialFilter = current.TryGetValue("partialFilterExpression", out var partial)
            ? partial.AsBsonDocument
            : null;
        return currentPartialFilter == expected.PartialFilter;
    }

    private static bool IsSupersededIndex(string migrationId, string indexName) =>
        migrationId switch
        {
            IdentityCredentialIndexMigrationId => indexName is
                "ix_refreshsessions_owner_active" or "ix_apikeys_owner_revoked_expires",
            IndexMigrationId => indexName is
                "ux_notifications_deduplication_key" or "ix_notifications_email_status_next_attempt",
            _ => false
        };

    private static string SerializeIndex(MongoIndexSpecification index) =>
        $"{index.Module}:{index.Collection}:{index.Name}:{index.Keys}:{index.Unique}:{index.CaseInsensitive}:{index.ExpireAfter}:{index.PartialFilter}";

    private async Task<MongoMigrationOutcome> ReplaceIdentityCredentialScalarUtcIndexesAsync(
        CancellationToken cancellationToken)
    {
        var indexes = MongoIdentityCredentialIndexes.All
            .Where(specification => specification.Name is
                "ix_refreshsessions_owner_active" or "ix_apikeys_owner_revoked_expires")
            .ToList();
        var checksum = Checksum(string.Join('|', indexes.Select(SerializeIndex)));
        var existing = await LoadLedgerAsync(
            IdentityCredentialScalarUtcIndexMigrationId,
            cancellationToken);
        if (existing is not null)
        {
            EnsureChecksum(existing, checksum);
            if (existing.State == MongoMigrationStates.Completed)
            {
                return ToOutcome(existing, MongoMigrationStates.Skipped);
            }
        }

        if (_options.DryRun)
        {
            return new MongoMigrationOutcome(
                IdentityCredentialScalarUtcIndexMigrationId,
                MongoMigrationStates.DryRun,
                indexes.Count);
        }

        var ledger = await GetOrCreateLedgerAsync(
            IdentityCredentialScalarUtcIndexMigrationId,
            checksum,
            cancellationToken);
        ledger = await AcquireLeaseAsync(ledger, cancellationToken);
        if (ledger.LeaseOwner != _owner)
        {
            return ToOutcome(ledger, MongoMigrationStates.Busy);
        }

        foreach (var specification in indexes)
        {
            var collection = mongo.GetCollection<BsonDocument>(
                specification.Collection,
                specification.Module);
            using var cursor = await collection.Indexes.ListAsync(cancellationToken);
            var current = (await cursor.ToListAsync(cancellationToken))
                .FirstOrDefault(index => index["name"].AsString == specification.Name);
            ledger.Examined++;
            if (current is not null && current["key"].AsBsonDocument == specification.Keys)
            {
                ledger.Skipped++;
                continue;
            }

            if (current is not null)
            {
                await collection.Indexes.DropOneAsync(specification.Name, cancellationToken);
            }

            await collection.Indexes.CreateOneAsync(
                new CreateIndexModel<BsonDocument>(
                    specification.Keys,
                    new CreateIndexOptions { Name = specification.Name }),
                cancellationToken: cancellationToken);
            ledger.Changed++;
            await SaveOwnedLedgerAsync(ledger, cancellationToken);
        }

        ledger.State = MongoMigrationStates.Completed;
        ledger.CompletedAt = DateTime.UtcNow;
        await SaveAndReleaseOwnedLedgerAsync(ledger, cancellationToken);
        return ToOutcome(ledger, MongoMigrationStates.Completed);
    }

    private async Task<MongoMigrationOutcome> ReplaceNotificationDeliveryIndexesAsync(
        CancellationToken cancellationToken)
    {
        var indexes = MongoNotificationDeliveryIndexes.All;
        var checksum = Checksum(string.Join('|', indexes.Select(SerializeIndex)));
        var existing = await LoadLedgerAsync(NotificationDeliveryIndexMigrationId, cancellationToken);
        if (existing is not null)
        {
            EnsureChecksum(existing, checksum);
            if (existing.State == MongoMigrationStates.Completed)
                return ToOutcome(existing, MongoMigrationStates.Skipped);
        }
        if (_options.DryRun)
            return new MongoMigrationOutcome(
                NotificationDeliveryIndexMigrationId,
                MongoMigrationStates.DryRun,
                indexes.Count);

        var ledger = await GetOrCreateLedgerAsync(
            NotificationDeliveryIndexMigrationId,
            checksum,
            cancellationToken);
        ledger = await AcquireLeaseAsync(ledger, cancellationToken);
        if (ledger.LeaseOwner != _owner)
            return ToOutcome(ledger, MongoMigrationStates.Busy);

        foreach (var specification in indexes)
        {
            var collection = mongo.GetCollection<BsonDocument>(
                specification.Collection,
                specification.Module);
            using var cursor = await collection.Indexes.ListAsync(cancellationToken);
            var current = (await cursor.ToListAsync(cancellationToken))
                .FirstOrDefault(index => index["name"].AsString == specification.Name);
            ledger.Examined++;
            if (current is not null)
                await collection.Indexes.DropOneAsync(specification.Name, cancellationToken);
            await collection.Indexes.CreateOneAsync(
                new CreateIndexModel<BsonDocument>(
                    specification.Keys,
                    new CreateIndexOptions<BsonDocument>
                    {
                        Name = specification.Name,
                        Unique = specification.Unique,
                        PartialFilterExpression = specification.PartialFilter
                    }),
                cancellationToken: cancellationToken);
            ledger.Changed++;
            await SaveOwnedLedgerAsync(ledger, cancellationToken);
        }

        ledger.State = MongoMigrationStates.Completed;
        ledger.CompletedAt = DateTime.UtcNow;
        await SaveAndReleaseOwnedLedgerAsync(ledger, cancellationToken);
        return ToOutcome(ledger, MongoMigrationStates.Completed);
    }
}
