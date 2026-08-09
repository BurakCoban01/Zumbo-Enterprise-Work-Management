using MongoDB.Bson;
using MongoDB.Driver;

internal sealed class MongoWorkItemTypeSchemaBackfill(
    IMongoMigrationExecutionContext context,
    string migrationId,
    string checksum,
    IReadOnlyList<string> defaultIssueTypeKeys)
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
            var count = await workItems.CountDocumentsAsync(WorkItemTypeSchemaFilter(BsonNull.Value), cancellationToken: cancellationToken);
            return new MongoMigrationOutcome(migrationId, MongoMigrationStates.DryRun, count);
        }
        var ledger = await context.GetOrCreateLedgerAsync(migrationId, checksum, cancellationToken);
        ledger = await context.AcquireLeaseAsync(ledger, cancellationToken);
        if (ledger.LeaseOwner != context.Owner)
        {
            return context.ToOutcome(ledger, MongoMigrationStates.Busy);
        }

        var schemas = mongo.GetCollection<BsonDocument>("workitemtypeschemas", WorkItemsModule);
        for (var batchNumber = 0; batchNumber < context.MaxBatches; batchNumber++)
        {
            var batch = await workItems.Find(WorkItemTypeSchemaFilter(ledger.Checkpoint)).Sort(new BsonDocument("_id", 1)).Limit(context.BatchSize).ToListAsync(cancellationToken);
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
                if (string.IsNullOrWhiteSpace(projectId))
                {
                    ledger.Skipped++;
                    continue;
                }

                var now = DateTime.UtcNow;
                await schemas.UpdateOneAsync(Builders<BsonDocument>.Filter.Eq("_id", projectId), new BsonDocument("$setOnInsert", new BsonDocument { ["_id"] = projectId, ["ProjectId"] = projectId, ["SchemaVersion"] = 1, ["IssueTypes"] = DefaultIssueTypes(), ["CustomFields"] = new BsonArray(), ["Layouts"] = DefaultIssueTypeLayouts(defaultIssueTypeKeys), ["CreatedAt"] = now, ["UpdatedAt"] = now, ["Version"] = 0 }), new UpdateOptions { IsUpsert = true }, cancellationToken);
                var issueType = context.StringValue(document, "Type") ?? "Task";
                if (!defaultIssueTypeKeys.Contains(issueType, StringComparer.OrdinalIgnoreCase))
                {
                    await schemas.UpdateOneAsync(Builders<BsonDocument>.Filter.Eq("_id", projectId) & Builders<BsonDocument>.Filter.Ne("IssueTypes.Key", issueType), Builders<BsonDocument>.Update.Push("IssueTypes", IssueType(issueType, issueType, "Standard", 100)).Push("Layouts", new BsonDocument { ["IssueTypeKey"] = issueType, ["FieldKeys"] = new BsonArray() }).Inc("SchemaVersion", 1).Inc("Version", 1).Set("UpdatedAt", now), cancellationToken: cancellationToken);
                }
                var version = Math.Max(context.NumericTicks(document.GetValue("Version", 0)), 0);
                var result = await workItems.UpdateOneAsync(MongoWorkflowLifecycleBackfill.WorkflowVersionForId(document["_id"], version), Builders<BsonDocument>.Update.Set("IssueTypeSchemaVersion", 1).Set("CustomFields", new BsonArray()).Set("WorkItemTypeSchemaMigratedBy", migrationId).Set("Version", version + 1), cancellationToken: cancellationToken);
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
    internal static FilterDefinition<BsonDocument> WorkItemTypeSchemaFilter(BsonValue checkpoint)
    {
        var filter = Builders<BsonDocument>.Filter.Exists("IssueTypeSchemaVersion", false)
            & Builders<BsonDocument>.Filter.Exists("CustomFields", false);
        if (!checkpoint.IsBsonNull)
        {
            filter &= Builders<BsonDocument>.Filter.Gt("_id", checkpoint);
        }

        return filter;
    }
    internal static BsonArray DefaultIssueTypes() => [IssueType("Epic", "Epic", "Epic", 0), IssueType("Story", "Story", "Standard", 10), IssueType("Task", "Task", "Standard", 20), IssueType("Bug", "Bug", "Standard", 30), IssueType("Subtask", "Subtask", "Subtask", 40)];
    internal static BsonArray DefaultIssueTypeLayouts(IReadOnlyList<string> defaultIssueTypeKeys) => new(defaultIssueTypeKeys.Select(key => new BsonDocument { ["IssueTypeKey"] = key, ["FieldKeys"] = new BsonArray() }));
    internal static BsonDocument IssueType(string key, string name, string hierarchy, int position) => new() { ["Key"] = key, ["Name"] = name, ["Description"] = string.Empty, ["HierarchyLevel"] = hierarchy, ["Active"] = true, ["Position"] = position };
}
