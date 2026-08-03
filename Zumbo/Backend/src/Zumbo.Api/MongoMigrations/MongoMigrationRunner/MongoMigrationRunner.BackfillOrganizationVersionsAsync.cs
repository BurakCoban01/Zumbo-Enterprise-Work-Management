using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using Zumbo.BuildingBlocks.Infrastructure.Persistence;

public sealed partial class MongoMigrationRunner{

    private async Task<MongoMigrationOutcome> BackfillOrganizationVersionsAsync(
        CancellationToken cancellationToken)
    {
        var existing = await LoadLedgerAsync(OrganizationVersionMigrationId, cancellationToken);
        if (existing is not null)
        {
            EnsureChecksum(existing, OrganizationVersionChecksum);
            if (existing.State == MongoMigrationStates.Completed)
            {
                return ToOutcome(existing, MongoMigrationStates.Skipped);
            }
        }

        var organizations = mongo.GetCollection<BsonDocument>("organizations", "Organizations");
        if (_options.DryRun)
        {
            var count = await organizations.CountDocumentsAsync(
                OrganizationVersionFilter(BsonNull.Value),
                cancellationToken: cancellationToken);
            return new MongoMigrationOutcome(
                OrganizationVersionMigrationId,
                MongoMigrationStates.DryRun,
                count);
        }

        var ledger = await GetOrCreateLedgerAsync(
            OrganizationVersionMigrationId,
            OrganizationVersionChecksum,
            cancellationToken);
        ledger = await AcquireLeaseAsync(ledger, cancellationToken);
        if (ledger.LeaseOwner != _owner)
        {
            return ToOutcome(ledger, MongoMigrationStates.Busy);
        }

        for (var batchNumber = 0; batchNumber < MaxBatches; batchNumber++)
        {
            var batch = await organizations.Find(OrganizationVersionFilter(ledger.Checkpoint))
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
                var updates = new List<UpdateDefinition<BsonDocument>>
                {
                    Builders<BsonDocument>.Update.Set("Version", 1L)
                };
                if (!document.Contains("Status") || document["Status"].IsBsonNull)
                {
                    updates.Add(Builders<BsonDocument>.Update.Set("Status", "Active"));
                }

                var result = await organizations.UpdateOneAsync(
                    OrganizationVersionForId(document["_id"]),
                    Builders<BsonDocument>.Update.Combine(updates),
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
