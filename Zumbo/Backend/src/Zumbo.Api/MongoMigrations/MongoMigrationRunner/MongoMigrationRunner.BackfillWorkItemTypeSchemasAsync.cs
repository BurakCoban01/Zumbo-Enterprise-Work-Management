using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using Zumbo.BuildingBlocks.Infrastructure.Persistence;

public sealed partial class MongoMigrationRunner{

    private async Task<MongoMigrationOutcome> BackfillWorkItemTypeSchemasAsync(
        CancellationToken cancellationToken)
    {
        var existing = await LoadLedgerAsync(WorkItemTypeSchemaMigrationId, cancellationToken);
        if (existing is not null)
        {
            EnsureChecksum(existing, WorkItemTypeSchemaChecksum);
            if (existing.State == MongoMigrationStates.Completed)
            {
                return ToOutcome(existing, MongoMigrationStates.Skipped);
            }
        }

        var workItems = mongo.GetCollection<BsonDocument>("workitems", WorkItemsModule);
        if (_options.DryRun)
        {
            var count = await workItems.CountDocumentsAsync(
                WorkItemTypeSchemaFilter(BsonNull.Value),
                cancellationToken: cancellationToken);
            return new MongoMigrationOutcome(
                WorkItemTypeSchemaMigrationId,
                MongoMigrationStates.DryRun,
                count);
        }

        var ledger = await GetOrCreateLedgerAsync(
            WorkItemTypeSchemaMigrationId,
            WorkItemTypeSchemaChecksum,
            cancellationToken);
        ledger = await AcquireLeaseAsync(ledger, cancellationToken);
        if (ledger.LeaseOwner != _owner)
        {
            return ToOutcome(ledger, MongoMigrationStates.Busy);
        }

        var schemas = mongo.GetCollection<BsonDocument>("workitemtypeschemas", WorkItemsModule);
        for (var batchNumber = 0; batchNumber < MaxBatches; batchNumber++)
        {
            var batch = await workItems.Find(WorkItemTypeSchemaFilter(ledger.Checkpoint))
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
                if (string.IsNullOrWhiteSpace(projectId))
                {
                    ledger.Skipped++;
                    continue;
                }

                var now = DateTime.UtcNow;
                await schemas.UpdateOneAsync(
                    Builders<BsonDocument>.Filter.Eq("_id", projectId),
                    new BsonDocument("$setOnInsert", new BsonDocument
                    {
                        ["_id"] = projectId,
                        ["ProjectId"] = projectId,
                        ["SchemaVersion"] = 1,
                        ["IssueTypes"] = DefaultIssueTypes(),
                        ["CustomFields"] = new BsonArray(),
                        ["Layouts"] = DefaultIssueTypeLayouts(),
                        ["CreatedAt"] = now,
                        ["UpdatedAt"] = now,
                        ["Version"] = 0
                    }),
                    new UpdateOptions { IsUpsert = true },
                    cancellationToken);

                var issueType = StringValue(document, "Type") ?? "Task";
                if (!DefaultIssueTypeKeys.Contains(issueType, StringComparer.OrdinalIgnoreCase))
                {
                    await schemas.UpdateOneAsync(
                        Builders<BsonDocument>.Filter.Eq("_id", projectId)
                        & Builders<BsonDocument>.Filter.Ne("IssueTypes.Key", issueType),
                        Builders<BsonDocument>.Update
                            .Push("IssueTypes", IssueType(issueType, issueType, "Standard", 100))
                            .Push("Layouts", new BsonDocument
                            {
                                ["IssueTypeKey"] = issueType,
                                ["FieldKeys"] = new BsonArray()
                            })
                            .Inc("SchemaVersion", 1)
                            .Inc("Version", 1)
                            .Set("UpdatedAt", now),
                        cancellationToken: cancellationToken);
                }

                var version = Math.Max(NumericTicks(document.GetValue("Version", 0)), 0);
                var result = await workItems.UpdateOneAsync(
                    WorkflowVersionForId(document["_id"], version),
                    Builders<BsonDocument>.Update
                        .Set("IssueTypeSchemaVersion", 1)
                        .Set("CustomFields", new BsonArray())
                        .Set("WorkItemTypeSchemaMigratedBy", WorkItemTypeSchemaMigrationId)
                        .Set("Version", version + 1),
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
