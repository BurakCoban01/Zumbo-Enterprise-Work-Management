using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using Zumbo.BuildingBlocks.Infrastructure.Persistence;

public sealed partial class MongoMigrationRunner{

    private async Task<MongoMigrationOutcome> BackfillApiKeyVersionsAsync(CancellationToken cancellationToken)
    {
        var existing = await LoadLedgerAsync(ApiKeyVersionMigrationId, cancellationToken);
        if (existing is not null)
        {
            EnsureChecksum(existing, ApiKeyVersionChecksum);
            if (existing.State == MongoMigrationStates.Completed)
            {
                return ToOutcome(existing, MongoMigrationStates.Skipped);
            }
        }

        var apiKeys = mongo.GetCollection<BsonDocument>("apikeys", "Identity");
        if (_options.DryRun)
        {
            var count = await apiKeys.CountDocumentsAsync(
                ApiKeyVersionFilter(BsonNull.Value),
                cancellationToken: cancellationToken);
            return new MongoMigrationOutcome(ApiKeyVersionMigrationId, MongoMigrationStates.DryRun, count);
        }

        var ledger = await GetOrCreateLedgerAsync(
            ApiKeyVersionMigrationId,
            ApiKeyVersionChecksum,
            cancellationToken);
        ledger = await AcquireLeaseAsync(ledger, cancellationToken);
        if (ledger.LeaseOwner != _owner)
        {
            return ToOutcome(ledger, MongoMigrationStates.Busy);
        }

        for (var batchNumber = 0; batchNumber < MaxBatches; batchNumber++)
        {
            var batch = await apiKeys.Find(ApiKeyVersionFilter(ledger.Checkpoint))
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
                var updates = new List<UpdateDefinition<BsonDocument>>();
                var version = document.GetValue("Version", BsonNull.Value);
                if (version.IsBsonNull || (version.IsNumeric && version.ToInt64() <= 0))
                {
                    updates.Add(Builders<BsonDocument>.Update.Set("Version", 1L));
                }

                if (!document.Contains("ExpiresAtUtc")
                    && TryResolveUtc(document.GetValue("ExpiresAt", BsonNull.Value), out var expiresAtUtc))
                {
                    updates.Add(Builders<BsonDocument>.Update.Set("ExpiresAtUtc", expiresAtUtc));
                }

                if (!document.Contains("RevokedAtUtc")
                    && TryResolveUtc(document.GetValue("RevokedAt", BsonNull.Value), out var revokedAtUtc))
                {
                    updates.Add(Builders<BsonDocument>.Update.Set("RevokedAtUtc", revokedAtUtc));
                }

                if (updates.Count == 0)
                {
                    ledger.Skipped++;
                    continue;
                }

                var result = await apiKeys.UpdateOneAsync(
                    ApiKeyVersionForId(document["_id"]),
                    Builders<BsonDocument>.Update.Combine(updates),
                    cancellationToken: cancellationToken);
                if (result.ModifiedCount == 1) ledger.Changed++; else ledger.Skipped++;
                if (ledger.Examined % 100 == 0)
                {
                    await SaveOwnedLedgerAsync(ledger, cancellationToken);
                }
            }

            ledger.Checkpoint = batch[^1]["_id"];
            ledger.State = MongoMigrationStates.Paused;
            await SaveOwnedLedgerAsync(ledger, cancellationToken);
        }

        await SaveAndReleaseOwnedLedgerAsync(ledger, cancellationToken);
        return ToOutcome(ledger, MongoMigrationStates.Paused);
    }
}
