using MongoDB.Bson;
using MongoDB.Driver;

internal sealed class MongoUserVersionBackfill(
    IMongoMigrationExecutionContext context,
    string migrationId,
    string checksum)
{
    internal async Task<MongoMigrationOutcome> ExecuteAsync(CancellationToken cancellationToken)
    {
        var mongo = context.Mongo;
        var existing = await context.LoadLedgerAsync(migrationId, cancellationToken);
        if (existing is not null)
        {
            context.EnsureChecksum(existing, checksum);
            if (existing.State == MongoMigrationStates.Completed)
            {
                return context.ToOutcome(existing, MongoMigrationStates.Skipped);
            }
        }

        var users = mongo.GetCollection<BsonDocument>("users", "Identity");
        if (context.Options.DryRun)
        {
            var count = await users.CountDocumentsAsync(
                UserVersionFilter(BsonNull.Value),
                cancellationToken: cancellationToken);
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
            var batch = await users.Find(UserVersionFilter(ledger.Checkpoint))
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
                var result = await users.UpdateOneAsync(
                    UserVersionForId(document["_id"]),
                    Builders<BsonDocument>.Update.Set("Version", 1L),
                    cancellationToken: cancellationToken);
                if (result.ModifiedCount == 1) ledger.Changed++; else ledger.Skipped++;
            }

            ledger.Checkpoint = batch[^1]["_id"];
            ledger.State = MongoMigrationStates.Paused;
            await context.SaveOwnedLedgerAsync(ledger, cancellationToken);
        }

        await context.SaveAndReleaseOwnedLedgerAsync(ledger, cancellationToken);
        return context.ToOutcome(ledger, MongoMigrationStates.Paused);
    }

    internal static FilterDefinition<BsonDocument> UserVersionFilter(BsonValue checkpoint)
    {
        var filter = Builders<BsonDocument>.Filter.Or(
            Builders<BsonDocument>.Filter.Exists("Version", false),
            Builders<BsonDocument>.Filter.Lte("Version", 0),
            Builders<BsonDocument>.Filter.Type("Version", BsonType.Null));
        if (!checkpoint.IsBsonNull)
        {
            filter &= Builders<BsonDocument>.Filter.Gt("_id", checkpoint);
        }

        return filter;
    }

    internal static FilterDefinition<BsonDocument> UserVersionForId(BsonValue id) =>
        Builders<BsonDocument>.Filter.Eq("_id", id)
        & Builders<BsonDocument>.Filter.Or(
            Builders<BsonDocument>.Filter.Exists("Version", false),
            Builders<BsonDocument>.Filter.Lte("Version", 0),
            Builders<BsonDocument>.Filter.Type("Version", BsonType.Null));
}
