using System.Security.Cryptography;
using System.Text;
using MongoDB.Bson;
using MongoDB.Driver;

using Zumbo.Api.Infrastructure.Persistence.MongoDb.Migrations.Abstractions;

namespace Zumbo.Api.Infrastructure.Persistence.MongoDb.Migrations.Definitions.Backfills;

internal sealed class MongoSprintLifecycleBackfill(
    IMongoMigrationExecutionContext context,
    string migrationId,
    string checksum)
{
    internal async Task<MongoMigrationOutcome> ExecuteAsync(CancellationToken cancellationToken)
    {
        var mongo = context.Mongo;
        const string WorkItemsModule = "WorkItems";
        var existing = await context.LoadLedgerAsync(migrationId, cancellationToken);
        if (existing is not null)
        {
            context.EnsureChecksum(existing, checksum);
            if (existing.State == MongoMigrationStates.Completed)
            {
                return context.ToOutcome(existing, MongoMigrationStates.Skipped);
            }
        }
        var workItems = mongo.GetCollection<BsonDocument>("workitems", WorkItemsModule);
        if (context.Options.DryRun)
        {
            var count = await workItems.CountDocumentsAsync(SprintLifecycleFilter(BsonNull.Value), cancellationToken: cancellationToken);
            return new MongoMigrationOutcome(migrationId, MongoMigrationStates.DryRun, count);
        }
        var ledger = await context.GetOrCreateLedgerAsync(migrationId, checksum, cancellationToken);
        ledger = await context.AcquireLeaseAsync(ledger, cancellationToken);
        if (ledger.LeaseOwner != context.Owner)
        {
            return context.ToOutcome(ledger, MongoMigrationStates.Busy);
        }

        var sprints = mongo.GetCollection<BsonDocument>("sprints", WorkItemsModule);
        for (var batchNumber = 0; batchNumber < context.MaxBatches; batchNumber++)
        {
            var batch = await workItems.Find(SprintLifecycleFilter(ledger.Checkpoint)).Sort(new BsonDocument("_id", 1)).Limit(context.BatchSize).ToListAsync(cancellationToken);
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
                var projectId = context.StringValue(document, "ProjectId") ?? string.Empty;
                var legacySprintId = context.StringValue(document, "SprintId") ?? string.Empty;
                if (string.IsNullOrWhiteSpace(projectId) || string.IsNullOrWhiteSpace(legacySprintId))
                {
                    ledger.Skipped++;
                    continue;
                }

                var sprintId = LegacySprintId(projectId, legacySprintId);
                var now = DateTime.UtcNow;
                await sprints.UpdateOneAsync(Builders<BsonDocument>.Filter.Eq("_id", sprintId), new BsonDocument("$setOnInsert", new BsonDocument { ["_id"] = sprintId, ["ProjectId"] = projectId, ["Name"] = $"{legacySprintId} (legacy-{sprintId[^8..]})", ["Goal"] = "Legacy sprint backfill", ["StartAtUtc"] = now, ["EndAtUtc"] = now.AddDays(13), ["Status"] = "Planned", ["CommittedItems"] = 0, ["CommittedPoints"] = 0, ["CompletedItems"] = 0, ["CompletedPoints"] = 0, ["CarryoverItems"] = 0, ["CarryoverPoints"] = 0, ["CreatedAt"] = now, ["UpdatedAt"] = now, ["Version"] = 0 }), new UpdateOptions { IsUpsert = true }, cancellationToken);
                var version = Math.Max(context.NumericTicks(document.GetValue("Version", 0)), 0);
                var update = Builders<BsonDocument>.Update.Set("SprintId", sprintId).Set("SprintLifecycleMigratedBy", migrationId).Set("Version", version + 1);
                var result = await workItems.UpdateOneAsync(MongoWorkflowLifecycleBackfill.WorkflowVersionForId(document["_id"], version), update, cancellationToken: cancellationToken);
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
    internal static FilterDefinition<BsonDocument> SprintLifecycleFilter(BsonValue checkpoint)
    {
        var filter = Builders<BsonDocument>.Filter.Exists("SprintId", true) & Builders<BsonDocument>.Filter.Ne("SprintId", BsonNull.Value) & Builders<BsonDocument>.Filter.Ne("SprintId", string.Empty) & Builders<BsonDocument>.Filter.Exists("SprintLifecycleMigratedBy", false);
        if (!checkpoint.IsBsonNull)
        {
            filter &= Builders<BsonDocument>.Filter.Gt("_id", checkpoint);
        }

        return filter;
    }
    internal static string LegacySprintId(string projectId, string sprintId) => "legacy-" + Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(projectId + ":" + sprintId))).ToLowerInvariant();
}
