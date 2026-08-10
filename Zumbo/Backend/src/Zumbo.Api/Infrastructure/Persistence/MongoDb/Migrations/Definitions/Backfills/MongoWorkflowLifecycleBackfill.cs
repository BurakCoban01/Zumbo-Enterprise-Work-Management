using MongoDB.Bson;
using MongoDB.Driver;

using Zumbo.Api.Infrastructure.Persistence.MongoDb.Migrations.Abstractions;

namespace Zumbo.Api.Infrastructure.Persistence.MongoDb.Migrations.Definitions.Backfills;

internal sealed class MongoWorkflowLifecycleBackfill(
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
        var workflows = mongo.GetCollection<BsonDocument>("workflows", "Workflows");
        if (context.Options.DryRun)
        {
            var count = await workflows.CountDocumentsAsync(WorkflowLifecycleFilter(BsonNull.Value), cancellationToken: cancellationToken);
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
            var batch = await workflows.Find(WorkflowLifecycleFilter(ledger.Checkpoint)).Sort(new BsonDocument("_id", 1)).Limit(context.BatchSize).ToListAsync(cancellationToken);
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
                var statuses = document.GetValue("Statuses", new BsonArray()).AsBsonArray;
                var transitions = document.GetValue("Transitions", new BsonArray()).AsBsonArray;
                var defaultStatus = statuses.Where(x => x.IsBsonDocument && x.AsBsonDocument.GetValue("Category", "") == "Todo").Select(x => x.AsBsonDocument.GetValue("Name", "To Do").AsString).FirstOrDefault() ?? "To Do";
                var done = new BsonArray(statuses.Where(x => x.IsBsonDocument && x.AsBsonDocument.GetValue("Category", "") == "Done").Select(x => x.AsBsonDocument.GetValue("Name", "Done")));
                var names = new BsonArray(statuses.Where(x => x.IsBsonDocument).Select(x => x.AsBsonDocument.GetValue("Name", "")));
                var schemes = new BsonArray { new BsonDocument { ["IssueType"] = "*", ["DefaultStatus"] = defaultStatus, ["Statuses"] = names, ["DoneStatuses"] = done } };
                var createdAt = document.GetValue("CreatedAt", DateTime.UtcNow);
                var published = new BsonDocument { ["Number"] = 1, ["State"] = "Published", ["Statuses"] = statuses, ["Transitions"] = transitions, ["IssueTypeSchemes"] = schemes, ["CreatedAt"] = createdAt, ["PublishedAt"] = document.GetValue("UpdatedAt", createdAt) };
                var update = Builders<BsonDocument>.Update.Set("Version", version + 1).Set("PublishedVersion", 1).Set("IssueTypeSchemes", schemes).Set("Draft", BsonNull.Value).Set("PublishedVersions", new BsonArray { published }).Set("WorkflowLifecycleMigratedBy", migrationId);
                var result = await workflows.UpdateOneAsync(WorkflowVersionForId(document["_id"], version), update, cancellationToken: cancellationToken);
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
    internal static FilterDefinition<BsonDocument> WorkflowLifecycleFilter(BsonValue checkpoint)
    {
        var filter = Builders<BsonDocument>.Filter.Or(Builders<BsonDocument>.Filter.Exists("PublishedVersion", false), Builders<BsonDocument>.Filter.Exists("IssueTypeSchemes", false), Builders<BsonDocument>.Filter.Exists("Draft", false), Builders<BsonDocument>.Filter.Exists("PublishedVersions", false));
        if (!checkpoint.IsBsonNull)
        {
            filter &= Builders<BsonDocument>.Filter.Gt("_id", checkpoint);
        }

        return filter;
    }
    internal static FilterDefinition<BsonDocument> WorkflowVersionForId(BsonValue id, long version)
    {
        var versionFilter = version == 0
            ? Builders<BsonDocument>.Filter.Or(
                Builders<BsonDocument>.Filter.Exists("Version", false),
                Builders<BsonDocument>.Filter.Eq("Version", 0))
            : Builders<BsonDocument>.Filter.Eq("Version", version);
        return Builders<BsonDocument>.Filter.Eq("_id", id) & versionFilter;
    }
}
