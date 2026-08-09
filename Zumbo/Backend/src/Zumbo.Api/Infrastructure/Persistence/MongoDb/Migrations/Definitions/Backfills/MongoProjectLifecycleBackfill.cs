using MongoDB.Bson;
using MongoDB.Driver;

internal sealed class MongoProjectLifecycleBackfill(
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
        var projects = mongo.GetCollection<BsonDocument>("projects", "Projects");
        if (context.Options.DryRun)
        {
            var count = await projects.CountDocumentsAsync(ProjectLifecycleFilter(BsonNull.Value), cancellationToken: cancellationToken);
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
            var batch = await projects.Find(ProjectLifecycleFilter(ledger.Checkpoint)).Sort(new BsonDocument("_id", 1)).Limit(context.BatchSize).ToListAsync(cancellationToken);
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
                var version = Math.Max(context.NumericTicks(document.GetValue("Version", 0)), 0);
                var updates = new List<UpdateDefinition<BsonDocument>>
                {
                    Builders<BsonDocument>.Update.Set("Version", version + 1),
                    Builders<BsonDocument>.Update.Set("ProjectLifecycleMigratedBy", migrationId)
                };
                AddProjectDefault(document, updates, "Visibility", "Internal");
                AddProjectDefault(document, updates, "Archived", false);
                AddProjectDefault(document, updates, "Members", new BsonArray());
                AddProjectDefault(document, updates, "TeamIds", new BsonArray());
                AddProjectDefault(document, updates, "Templates", new BsonArray());
                AddProjectDefault(document, updates, "Components", new BsonArray());
                AddProjectDefault(document, updates, "Versions", new BsonArray());
                AddProjectDefault(document, updates, "Releases", new BsonArray());
                AddProjectDefault(document, updates, "Milestones", new BsonArray());
                AddProjectDefault(document, updates, "ArchivedAt", BsonNull.Value);
                AddProjectDefault(document, updates, "RetainUntil", BsonNull.Value);
                var result = await projects.UpdateOneAsync(ProjectVersionForId(document["_id"], version), Builders<BsonDocument>.Update.Combine(updates), cancellationToken: cancellationToken);
                if (result.ModifiedCount == 1)
                {
                    ledger.Changed++;
                }
                else
                {
                    ledger.Skipped++;
                }
            }
            ledger.Checkpoint = batch[^1]["_id"];
            ledger.State = MongoMigrationStates.Paused;
            await context.SaveOwnedLedgerAsync(ledger, cancellationToken);
        }
        await context.SaveAndReleaseOwnedLedgerAsync(ledger, cancellationToken);
        return context.ToOutcome(ledger, MongoMigrationStates.Paused);
    }

    internal static FilterDefinition<BsonDocument> ProjectLifecycleFilter(BsonValue checkpoint)
    {
        var filter = Builders<BsonDocument>.Filter.Or(Builders<BsonDocument>.Filter.Exists("Version", false), Builders<BsonDocument>.Filter.Lte("Version", 0), Builders<BsonDocument>.Filter.Type("Version", BsonType.Null), Builders<BsonDocument>.Filter.Exists("Visibility", false), Builders<BsonDocument>.Filter.Exists("Templates", false), Builders<BsonDocument>.Filter.Exists("Components", false), Builders<BsonDocument>.Filter.Exists("Versions", false), Builders<BsonDocument>.Filter.Exists("Releases", false), Builders<BsonDocument>.Filter.Exists("Milestones", false), Builders<BsonDocument>.Filter.Exists("ArchivedAt", false), Builders<BsonDocument>.Filter.Exists("RetainUntil", false));
        if (!checkpoint.IsBsonNull)
        {
            filter &= Builders<BsonDocument>.Filter.Gt("_id", checkpoint);
        }
        return filter;
    }
    internal static FilterDefinition<BsonDocument> ProjectVersionForId(BsonValue id, long version)
    {
        var versionFilter = version == 0 ? Builders<BsonDocument>.Filter.Or(Builders<BsonDocument>.Filter.Exists("Version", false), Builders<BsonDocument>.Filter.Eq("Version", 0), Builders<BsonDocument>.Filter.Type("Version", BsonType.Null)) : Builders<BsonDocument>.Filter.Eq("Version", version);
        return Builders<BsonDocument>.Filter.Eq("_id", id) & versionFilter;
    }
    internal static void AddProjectDefault(BsonDocument document, ICollection<UpdateDefinition<BsonDocument>> updates, string field, BsonValue value)
    {
        if (!document.Contains(field) || document[field].IsBsonNull)
        {
            updates.Add(Builders<BsonDocument>.Update.Set(field, value));
        }
    }
}
