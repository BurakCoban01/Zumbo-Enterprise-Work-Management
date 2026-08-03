using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using Zumbo.BuildingBlocks.Infrastructure.Persistence;

public sealed partial class MongoMigrationRunner{

    private async Task<MongoMigrationOutcome> BackfillWorkItemGraphAsync(
        CancellationToken cancellationToken)
    {
        var existing = await LoadLedgerAsync(WorkItemGraphMigrationId, cancellationToken);
        if (existing is not null)
        {
            EnsureChecksum(existing, WorkItemGraphChecksum);
            if (existing.State == MongoMigrationStates.Completed)
            {
                return ToOutcome(existing, MongoMigrationStates.Skipped);
            }
        }

        var workItems = mongo.GetCollection<BsonDocument>("workitems", WorkItemsModule);
        if (_options.DryRun)
        {
            var count = await workItems.CountDocumentsAsync(
                WorkItemGraphFilter(BsonNull.Value),
                cancellationToken: cancellationToken);
            return new MongoMigrationOutcome(
                WorkItemGraphMigrationId,
                MongoMigrationStates.DryRun,
                count);
        }

        var ledger = await GetOrCreateLedgerAsync(
            WorkItemGraphMigrationId,
            WorkItemGraphChecksum,
            cancellationToken);
        ledger = await AcquireLeaseAsync(ledger, cancellationToken);
        if (ledger.LeaseOwner != _owner)
        {
            return ToOutcome(ledger, MongoMigrationStates.Busy);
        }

        var edges = mongo.GetCollection<BsonDocument>("workitemrelationedges", WorkItemsModule);
        for (var batchNumber = 0; batchNumber < MaxBatches; batchNumber++)
        {
            var batch = await workItems.Find(WorkItemGraphFilter(ledger.Checkpoint))
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

            foreach (var workItem in batch)
            {
                ledger.Examined++;
                var sourceId = StringValue(workItem, "_id");
                var projectId = StringValue(workItem, "ProjectId");
                if (string.IsNullOrWhiteSpace(sourceId) || string.IsNullOrWhiteSpace(projectId))
                {
                    ledger.Skipped++;
                    continue;
                }

                foreach (var value in ArrayValue(workItem, "Relations"))
                {
                    if (!value.IsBsonDocument)
                    {
                        ledger.Skipped++;
                        continue;
                    }

                    var relation = value.AsBsonDocument;
                    var targetId = StringValue(relation, "RelatedWorkItemId");
                    var relationType = NormalizeGraphRelationType(StringValue(relation, "RelationType"));
                    if (string.IsNullOrWhiteSpace(targetId) || relationType is null)
                    {
                        ledger.Skipped++;
                        continue;
                    }

                    var (dependencyFrom, dependencyTo) = relationType switch
                    {
                        "Blocks" => (sourceId, targetId),
                        "BlockedBy" => (targetId, sourceId),
                        _ => ((string?)null, (string?)null)
                    };
                    var id = WorkItemRelationEdgeId(projectId, sourceId, targetId, relationType);
                    var edge = new BsonDocument
                    {
                        ["_id"] = id,
                        ["ProjectId"] = projectId,
                        ["SourceWorkItemId"] = sourceId,
                        ["TargetWorkItemId"] = targetId,
                        ["RelationType"] = relationType,
                        ["DependencyFromWorkItemId"] = dependencyFrom is null ? BsonNull.Value : dependencyFrom,
                        ["DependencyToWorkItemId"] = dependencyTo is null ? BsonNull.Value : dependencyTo,
                        ["CreatedAt"] = DateTime.UtcNow,
                        ["Version"] = 0L
                    };
                    var result = await edges.UpdateOneAsync(
                        Builders<BsonDocument>.Filter.Eq("_id", id),
                        new BsonDocument("$setOnInsert", edge),
                        new UpdateOptions { IsUpsert = true },
                        cancellationToken);
                    if (result.UpsertedId is null) ledger.Skipped++; else ledger.Changed++;
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
