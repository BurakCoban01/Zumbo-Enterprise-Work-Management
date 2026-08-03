using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using Zumbo.BuildingBlocks.Infrastructure.Persistence;

public sealed partial class MongoMigrationRunner
{
    private async Task<MongoMigrationOutcome> BackfillUserVersionsAsync(CancellationToken cancellationToken)
    {
        var existing = await LoadLedgerAsync(UserVersionMigrationId, cancellationToken);
        if (existing is not null)
        {
            EnsureChecksum(existing, UserVersionChecksum);
            if (existing.State == MongoMigrationStates.Completed)
            {
                return ToOutcome(existing, MongoMigrationStates.Skipped);
            }
        }

        var users = mongo.GetCollection<BsonDocument>("users", "Identity");
        if (_options.DryRun)
        {
            var count = await users.CountDocumentsAsync(
                UserVersionFilter(BsonNull.Value),
                cancellationToken: cancellationToken);
            return new MongoMigrationOutcome(UserVersionMigrationId, MongoMigrationStates.DryRun, count);
        }

        var ledger = await GetOrCreateLedgerAsync(
            UserVersionMigrationId,
            UserVersionChecksum,
            cancellationToken);
        ledger = await AcquireLeaseAsync(ledger, cancellationToken);
        if (ledger.LeaseOwner != _owner)
        {
            return ToOutcome(ledger, MongoMigrationStates.Busy);
        }

        for (var batchNumber = 0; batchNumber < MaxBatches; batchNumber++)
        {
            var batch = await users.Find(UserVersionFilter(ledger.Checkpoint))
                .Sort(new BsonDocument("_id", 1))
                .Limit(BatchSize)
                .ToListAsync(cancellationToken);
            if (batch.Count == 0)
            {
                ledger.State = MongoMigrationStates.Completed;
                ledger.CompletedAt = DateTime.UtcNow;
                await SaveAndReleaseOwnedLedgerAsync(ledger, cancellationToken);
                return ToOutcome(ledger, MongoMigrationStates.Completed);
            }

            foreach (var document in batch)
            {
                ledger.Examined++;
                var result = await users.UpdateOneAsync(
                    UserVersionForId(document["_id"]),
                    Builders<BsonDocument>.Update.Set("Version", 1L),
                    cancellationToken: cancellationToken);
                if (result.ModifiedCount == 1) ledger.Changed++; else ledger.Skipped++;
            }

            ledger.Checkpoint = batch[^1]["_id"];
            ledger.State = MongoMigrationStates.Paused;
            await SaveOwnedLedgerAsync(ledger, cancellationToken);
        }

        await SaveAndReleaseOwnedLedgerAsync(ledger, cancellationToken);
        return ToOutcome(ledger, MongoMigrationStates.Paused);
    }
}
