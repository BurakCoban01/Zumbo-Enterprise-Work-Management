using System.Security.Cryptography;
using System.Text;
using MongoDB.Bson;
using MongoDB.Driver;

using Zumbo.Api.Infrastructure.Persistence.MongoDb.Migrations.Abstractions;

namespace Zumbo.Api.Infrastructure.Persistence.MongoDb.Migrations.Definitions.Backfills;

internal sealed class MongoWorkItemGraphBackfill(
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
            var count = await workItems.CountDocumentsAsync(WorkItemGraphFilter(BsonNull.Value), cancellationToken: cancellationToken);
            return new MongoMigrationOutcome(migrationId, MongoMigrationStates.DryRun, count);
        }

        var ledger = await context.GetOrCreateLedgerAsync(migrationId, checksum, cancellationToken);
        ledger = await context.AcquireLeaseAsync(ledger, cancellationToken);
        if (ledger.LeaseOwner != context.Owner)
        {
            return context.ToOutcome(ledger, MongoMigrationStates.Busy);
        }

        var edges = mongo.GetCollection<BsonDocument>("workitemrelationedges", WorkItemsModule);
        for (var batchNumber = 0; batchNumber < context.MaxBatches; batchNumber++)
        {
            var batch = await workItems.Find(WorkItemGraphFilter(ledger.Checkpoint)).Sort(new BsonDocument("_id", 1)).Limit(context.BatchSize).ToListAsync(cancellationToken);
            if (batch.Count == 0)
            {
                ledger.State = MongoMigrationStates.Completed;
                ledger.CompletedAt = DateTime.UtcNow;
                await context.SaveAndReleaseOwnedLedgerAsync(ledger, cancellationToken);
                return context.ToOutcome(ledger, MongoMigrationStates.Completed);
            }
            foreach (var workItem in batch)
            {
                ledger.Examined++;
                var sourceId = context.StringValue(workItem, "_id");
                var projectId = context.StringValue(workItem, "ProjectId");
                if (string.IsNullOrWhiteSpace(sourceId) || string.IsNullOrWhiteSpace(projectId))
                {
                    ledger.Skipped++;
                    continue;
                }

                foreach (var value in context.ArrayValue(workItem, "Relations"))
                {
                    if (!value.IsBsonDocument)
                    {
                        ledger.Skipped++;
                        continue;
                    }

                    var relation = value.AsBsonDocument;
                    var targetId = context.StringValue(relation, "RelatedWorkItemId");
                    var relationType = NormalizeGraphRelationType(context.StringValue(relation, "RelationType"));
                    if (string.IsNullOrWhiteSpace(targetId) || relationType is null)
                    {
                        ledger.Skipped++;
                        continue;
                    }

                    var (dependencyFrom, dependencyTo) = relationType switch { "Blocks" => (sourceId, targetId), "BlockedBy" => (targetId, sourceId), _ => ((string?)null, (string?)null) };
                    var id = WorkItemRelationEdgeId(projectId, sourceId, targetId, relationType);
                    var edge = new BsonDocument { ["_id"] = id, ["ProjectId"] = projectId, ["SourceWorkItemId"] = sourceId, ["TargetWorkItemId"] = targetId, ["RelationType"] = relationType, ["DependencyFromWorkItemId"] = dependencyFrom is null ? BsonNull.Value : dependencyFrom, ["DependencyToWorkItemId"] = dependencyTo is null ? BsonNull.Value : dependencyTo, ["CreatedAt"] = DateTime.UtcNow, ["Version"] = 0L };
                    var result = await edges.UpdateOneAsync(Builders<BsonDocument>.Filter.Eq("_id", id), new BsonDocument("$setOnInsert", edge), new UpdateOptions { IsUpsert = true }, cancellationToken);
                    if (result.UpsertedId is null)
                    {
                        ledger.Skipped++;
                    }
                    else
                    {
                        ledger.Changed++;
                    }
                }
            }
            ledger.Checkpoint = batch[^1]["_id"];
            ledger.State = MongoMigrationStates.Paused;
            await context.SaveOwnedLedgerAsync(ledger, cancellationToken);
        }

        await context.SaveAndReleaseOwnedLedgerAsync(ledger, cancellationToken);
        return context.ToOutcome(ledger, MongoMigrationStates.Paused);
    }
    internal static FilterDefinition<BsonDocument> WorkItemGraphFilter(BsonValue checkpoint)
    {
        var filter = Builders<BsonDocument>.Filter.Exists("Relations.0", true);
        if (!checkpoint.IsBsonNull)
        {
            filter &= Builders<BsonDocument>.Filter.Gt("_id", checkpoint);
        }

        return filter;
    }
    internal static string WorkItemRelationEdgeId(string projectId, string sourceWorkItemId, string targetWorkItemId, string relationType) => Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes($"{projectId}\n{sourceWorkItemId}\n{targetWorkItemId}\n{relationType}"))).ToLowerInvariant();
    internal static string? NormalizeGraphRelationType(string? relationType) => relationType?.Trim().ToLowerInvariant() switch { "blocks" => "Blocks", "blockedby" or "blocked-by" => "BlockedBy", "relatesto" or "relates-to" => "RelatesTo", "duplicates" => "Duplicates", _ => null };
}
