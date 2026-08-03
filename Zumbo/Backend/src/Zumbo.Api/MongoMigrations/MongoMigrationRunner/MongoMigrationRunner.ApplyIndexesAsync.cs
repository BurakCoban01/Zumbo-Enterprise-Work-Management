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
}
