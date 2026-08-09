using MongoDB.Bson;
using MongoDB.Driver;

internal sealed class MongoApiKeyVersionBackfill(
    IMongoMigrationExecutionContext context,
    string migrationId,
    string checksum)
{
    public async Task<MongoMigrationOutcome> ExecuteAsync(CancellationToken cancellationToken)
    {
        var existing = await context.LoadLedgerAsync(migrationId, cancellationToken);
        if (existing is not null)
        {
            context.EnsureChecksum(existing, checksum);
            if (existing.State == MongoMigrationStates.Completed)
            {
                return context.ToOutcome(existing, MongoMigrationStates.Skipped);
            }
        }

        var mongo = context.Mongo;
        var apiKeys = mongo.GetCollection<BsonDocument>("apikeys", "Identity");
        if (context.Options.DryRun)
        {
            var count = await apiKeys.CountDocumentsAsync(Filter(BsonNull.Value), cancellationToken: cancellationToken);
            return new MongoMigrationOutcome(migrationId, MongoMigrationStates.DryRun, count);
        }

        var ledger = await context.GetOrCreateLedgerAsync(migrationId, checksum, cancellationToken);
        ledger = await context.AcquireLeaseAsync(ledger, cancellationToken);
        if (ledger.LeaseOwner != context.Owner)
        {
            return context.ToOutcome(ledger, MongoMigrationStates.Busy);
        }

        for (var batchNumber = 0; batchNumber < context.MaxBatches; batchNumber++)
        {
            var batch = await apiKeys.Find(Filter(ledger.Checkpoint))
                .Sort(new BsonDocument("_id", 1))
                .Limit(context.BatchSize)
                .ToListAsync(cancellationToken);
            if (batch.Count == 0)
            {
                ledger.State = MongoMigrationStates.Completed;
                ledger.CompletedAt = DateTime.UtcNow;
                await context.SaveAndReleaseOwnedLedgerAsync(ledger, cancellationToken);
                return context.ToOutcome(ledger, MongoMigrationStates.Completed);
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
                    && context.TryResolveUtc(document.GetValue("ExpiresAt", BsonNull.Value), out var expiresAtUtc))
                {
                    updates.Add(Builders<BsonDocument>.Update.Set("ExpiresAtUtc", expiresAtUtc));
                }

                if (!document.Contains("RevokedAtUtc")
                    && context.TryResolveUtc(document.GetValue("RevokedAt", BsonNull.Value), out var revokedAtUtc))
                {
                    updates.Add(Builders<BsonDocument>.Update.Set("RevokedAtUtc", revokedAtUtc));
                }

                if (updates.Count == 0)
                {
                    ledger.Skipped++;
                    continue;
                }

                var result = await apiKeys.UpdateOneAsync(
                    ForId(document["_id"]),
                    Builders<BsonDocument>.Update.Combine(updates),
                    cancellationToken: cancellationToken);
                if (result.ModifiedCount == 1) ledger.Changed++; else ledger.Skipped++;
                if (ledger.Examined % 100 == 0)
                {
                    await context.SaveOwnedLedgerAsync(ledger, cancellationToken);
                }
            }

            ledger.Checkpoint = batch[^1]["_id"];
            ledger.State = MongoMigrationStates.Paused;
            await context.SaveOwnedLedgerAsync(ledger, cancellationToken);
        }

        await context.SaveAndReleaseOwnedLedgerAsync(ledger, cancellationToken);
        return context.ToOutcome(ledger, MongoMigrationStates.Paused);
    }

    internal static FilterDefinition<BsonDocument> Filter(BsonValue checkpoint)
    {
        var filter = Builders<BsonDocument>.Filter.Or(
            Builders<BsonDocument>.Filter.Exists("Version", false),
            Builders<BsonDocument>.Filter.Lte("Version", 0),
            Builders<BsonDocument>.Filter.And(
                Builders<BsonDocument>.Filter.Exists("ExpiresAt", true),
                Builders<BsonDocument>.Filter.Exists("ExpiresAtUtc", false)),
            Builders<BsonDocument>.Filter.And(
                Builders<BsonDocument>.Filter.Ne("RevokedAt", BsonNull.Value),
                Builders<BsonDocument>.Filter.Exists("RevokedAtUtc", false)));
        if (!checkpoint.IsBsonNull)
        {
            filter &= Builders<BsonDocument>.Filter.Gt("_id", checkpoint);
        }

        return filter;
    }

    internal static FilterDefinition<BsonDocument> ForId(BsonValue id) =>
        Builders<BsonDocument>.Filter.Eq("_id", id)
        & Builders<BsonDocument>.Filter.Or(
            Builders<BsonDocument>.Filter.Exists("Version", false),
            Builders<BsonDocument>.Filter.Lte("Version", 0),
            Builders<BsonDocument>.Filter.And(
                Builders<BsonDocument>.Filter.Exists("ExpiresAt", true),
                Builders<BsonDocument>.Filter.Exists("ExpiresAtUtc", false)),
            Builders<BsonDocument>.Filter.And(
                Builders<BsonDocument>.Filter.Ne("RevokedAt", BsonNull.Value),
                Builders<BsonDocument>.Filter.Exists("RevokedAtUtc", false)));
}
