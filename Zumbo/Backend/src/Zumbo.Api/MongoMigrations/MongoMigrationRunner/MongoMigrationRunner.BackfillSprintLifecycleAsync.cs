using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using Zumbo.BuildingBlocks.Infrastructure.Persistence;

public sealed partial class MongoMigrationRunner{

    private async Task<MongoMigrationOutcome> BackfillSprintLifecycleAsync(
        CancellationToken cancellationToken)
    {
        var existing = await LoadLedgerAsync(SprintLifecycleMigrationId, cancellationToken);
        if (existing is not null)
        {
            EnsureChecksum(existing, SprintLifecycleChecksum);
            if (existing.State == MongoMigrationStates.Completed)
            {
                return ToOutcome(existing, MongoMigrationStates.Skipped);
            }
        }

        var workItems = mongo.GetCollection<BsonDocument>("workitems", WorkItemsModule);
        if (_options.DryRun)
        {
            var count = await workItems.CountDocumentsAsync(
                SprintLifecycleFilter(BsonNull.Value),
                cancellationToken: cancellationToken);
            return new MongoMigrationOutcome(SprintLifecycleMigrationId, MongoMigrationStates.DryRun, count);
        }

        var ledger = await GetOrCreateLedgerAsync(
            SprintLifecycleMigrationId,
            SprintLifecycleChecksum,
            cancellationToken);
        ledger = await AcquireLeaseAsync(ledger, cancellationToken);
        if (ledger.LeaseOwner != _owner)
        {
            return ToOutcome(ledger, MongoMigrationStates.Busy);
        }

        var sprints = mongo.GetCollection<BsonDocument>("sprints", WorkItemsModule);
        for (var batchNumber = 0; batchNumber < MaxBatches; batchNumber++)
        {
            var batch = await workItems.Find(SprintLifecycleFilter(ledger.Checkpoint))
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
                var projectId = StringValue(document, "ProjectId") ?? string.Empty;
                var legacySprintId = StringValue(document, "SprintId") ?? string.Empty;
                if (string.IsNullOrWhiteSpace(projectId) || string.IsNullOrWhiteSpace(legacySprintId))
                {
                    ledger.Skipped++;
                    continue;
                }

                var sprintId = LegacySprintId(projectId, legacySprintId);
                var now = DateTime.UtcNow;
                await sprints.UpdateOneAsync(
                    Builders<BsonDocument>.Filter.Eq("_id", sprintId),
                    new BsonDocument("$setOnInsert", new BsonDocument
                    {
                        ["_id"] = sprintId,
                        ["ProjectId"] = projectId,
                        ["Name"] = $"{legacySprintId} (legacy-{sprintId[^8..]})",
                        ["Goal"] = "Legacy sprint backfill",
                        ["StartAtUtc"] = now,
                        ["EndAtUtc"] = now.AddDays(13),
                        ["Status"] = "Planned",
                        ["CommittedItems"] = 0,
                        ["CommittedPoints"] = 0,
                        ["CompletedItems"] = 0,
                        ["CompletedPoints"] = 0,
                        ["CarryoverItems"] = 0,
                        ["CarryoverPoints"] = 0,
                        ["CreatedAt"] = now,
                        ["UpdatedAt"] = now,
                        ["Version"] = 0
                    }),
                    new UpdateOptions { IsUpsert = true },
                    cancellationToken);

                var version = Math.Max(NumericTicks(document.GetValue("Version", 0)), 0);
                var update = Builders<BsonDocument>.Update
                    .Set("SprintId", sprintId)
                    .Set("SprintLifecycleMigratedBy", SprintLifecycleMigrationId)
                    .Set("Version", version + 1);
                var result = await workItems.UpdateOneAsync(
                    WorkflowVersionForId(document["_id"], version),
                    update,
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
