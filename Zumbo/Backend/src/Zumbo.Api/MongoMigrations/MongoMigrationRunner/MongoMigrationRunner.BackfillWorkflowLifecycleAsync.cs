using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using Zumbo.BuildingBlocks.Infrastructure.Persistence;

public sealed partial class MongoMigrationRunner{

    private async Task<MongoMigrationOutcome> BackfillWorkflowLifecycleAsync(
        CancellationToken cancellationToken)
    {
        var existing = await LoadLedgerAsync(WorkflowLifecycleMigrationId, cancellationToken);
        if (existing is not null)
        {
            EnsureChecksum(existing, WorkflowLifecycleChecksum);
            if (existing.State == MongoMigrationStates.Completed)
            {
                return ToOutcome(existing, MongoMigrationStates.Skipped);
            }
        }

        var workflows = mongo.GetCollection<BsonDocument>("workflows", "Workflows");
        if (_options.DryRun)
        {
            var count = await workflows.CountDocumentsAsync(
                WorkflowLifecycleFilter(BsonNull.Value),
                cancellationToken: cancellationToken);
            return new MongoMigrationOutcome(WorkflowLifecycleMigrationId, MongoMigrationStates.DryRun, count);
        }

        var ledger = await GetOrCreateLedgerAsync(
            WorkflowLifecycleMigrationId,
            WorkflowLifecycleChecksum,
            cancellationToken);
        ledger = await AcquireLeaseAsync(ledger, cancellationToken);
        if (ledger.LeaseOwner != _owner)
        {
            return ToOutcome(ledger, MongoMigrationStates.Busy);
        }

        for (var batchNumber = 0; batchNumber < MaxBatches; batchNumber++)
        {
            var batch = await workflows.Find(WorkflowLifecycleFilter(ledger.Checkpoint))
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
                var version = Math.Max(NumericTicks(document.GetValue("Version", 0)), 0);
                var statuses = document.GetValue("Statuses", new BsonArray()).AsBsonArray;
                var transitions = document.GetValue("Transitions", new BsonArray()).AsBsonArray;
                var defaultStatus = statuses
                    .Where(x => x.IsBsonDocument && x.AsBsonDocument.GetValue("Category", "") == "Todo")
                    .Select(x => x.AsBsonDocument.GetValue("Name", "To Do").AsString)
                    .FirstOrDefault() ?? "To Do";
                var done = new BsonArray(statuses
                    .Where(x => x.IsBsonDocument && x.AsBsonDocument.GetValue("Category", "") == "Done")
                    .Select(x => x.AsBsonDocument.GetValue("Name", "Done")));
                var names = new BsonArray(statuses
                    .Where(x => x.IsBsonDocument)
                    .Select(x => x.AsBsonDocument.GetValue("Name", "")));
                var schemes = new BsonArray
                {
                    new BsonDocument
                    {
                        ["IssueType"] = "*",
                        ["DefaultStatus"] = defaultStatus,
                        ["Statuses"] = names,
                        ["DoneStatuses"] = done
                    }
                };
                var createdAt = document.GetValue("CreatedAt", DateTime.UtcNow);
                var published = new BsonDocument
                {
                    ["Number"] = 1,
                    ["State"] = "Published",
                    ["Statuses"] = statuses,
                    ["Transitions"] = transitions,
                    ["IssueTypeSchemes"] = schemes,
                    ["CreatedAt"] = createdAt,
                    ["PublishedAt"] = document.GetValue("UpdatedAt", createdAt)
                };
                var update = Builders<BsonDocument>.Update
                    .Set("Version", version + 1)
                    .Set("PublishedVersion", 1)
                    .Set("IssueTypeSchemes", schemes)
                    .Set("Draft", BsonNull.Value)
                    .Set("PublishedVersions", new BsonArray { published })
                    .Set("WorkflowLifecycleMigratedBy", WorkflowLifecycleMigrationId);
                var result = await workflows.UpdateOneAsync(
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
