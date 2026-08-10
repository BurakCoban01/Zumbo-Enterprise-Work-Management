using MongoDB.Bson;
using MongoDB.Driver;

using Zumbo.Api.Infrastructure.Persistence.MongoDb.Migrations.Abstractions;

namespace Zumbo.Api.Infrastructure.Persistence.MongoDb.Migrations.Definitions.Backfills;

internal sealed class MongoOrganizationVersionBackfill(
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
        var organizations = mongo.GetCollection<BsonDocument>("organizations", "Organizations");
        if (context.Options.DryRun)
        {
            var count = await organizations.CountDocumentsAsync(
                Filter(BsonNull.Value),
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
            var batch = await organizations.Find(Filter(ledger.Checkpoint))
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
                var updates = new List<UpdateDefinition<BsonDocument>>
                {
                    Builders<BsonDocument>.Update.Set("Version", 1L)
                };
                if (!document.Contains("Status") || document["Status"].IsBsonNull)
                {
                    updates.Add(Builders<BsonDocument>.Update.Set("Status", "Active"));
                }

                var result = await organizations.UpdateOneAsync(
                    ForId(document["_id"]),
                    Builders<BsonDocument>.Update.Combine(updates),
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

    internal static FilterDefinition<BsonDocument> Filter(BsonValue checkpoint)
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

    internal static FilterDefinition<BsonDocument> ForId(BsonValue id) =>
        Builders<BsonDocument>.Filter.Eq("_id", id)
        & Builders<BsonDocument>.Filter.Or(
            Builders<BsonDocument>.Filter.Exists("Version", false),
            Builders<BsonDocument>.Filter.Lte("Version", 0),
            Builders<BsonDocument>.Filter.Type("Version", BsonType.Null));
}
