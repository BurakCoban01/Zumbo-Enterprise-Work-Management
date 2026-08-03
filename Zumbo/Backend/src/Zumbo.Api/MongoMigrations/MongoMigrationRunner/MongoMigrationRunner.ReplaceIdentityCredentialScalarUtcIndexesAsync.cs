using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using Zumbo.BuildingBlocks.Infrastructure.Persistence;

public sealed partial class MongoMigrationRunner{

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
}
