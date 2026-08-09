using System.Security.Cryptography;
using System.Text;
using MongoDB.Bson;
using MongoDB.Driver;

internal sealed class MongoWorkItemActivityBackfill(
    IMongoMigrationExecutionContext context,
    string migrationId,
    string checksum)
{
    private const string WorkItemsModule = "WorkItems";

    internal async Task<MongoMigrationOutcome> ExecuteAsync(CancellationToken cancellationToken)
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
        var workItems = mongo.GetCollection<BsonDocument>("workitems", WorkItemsModule);
        if (context.Options.DryRun)
        {
            var count = await workItems.CountDocumentsAsync(
                WorkItemActivityFilter(BsonNull.Value),
                cancellationToken: cancellationToken);
            return new MongoMigrationOutcome(migrationId, MongoMigrationStates.DryRun, count);
        }

        var ledger = await context.GetOrCreateLedgerAsync(migrationId, checksum, cancellationToken);
        ledger = await context.AcquireLeaseAsync(ledger, cancellationToken);
        if (ledger.LeaseOwner != context.Owner)
        {
            return context.ToOutcome(ledger, MongoMigrationStates.Busy);
        }

        var projects = mongo.GetCollection<BsonDocument>("projects", "Projects");
        for (var batchNumber = 0; batchNumber < context.MaxBatches; batchNumber++)
        {
            var batch = await workItems.Find(WorkItemActivityFilter(ledger.Checkpoint))
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

            foreach (var workItem in batch)
            {
                ledger.Examined++;
                var workItemId = workItem["_id"].ToString() ?? string.Empty;
                var projectId = context.StringValue(workItem, "ProjectId") ?? string.Empty;
                if (string.IsNullOrWhiteSpace(workItemId))
                {
                    throw new InvalidOperationException(
                        "A work item with an empty identifier cannot be migrated.");
                }

                if (!HasMigratableActivities(workItem))
                {
                    ledger.Skipped++;
                    await context.SaveOwnedLedgerAsync(ledger, cancellationToken);
                    continue;
                }

                var project = await projects.Find(Builders<BsonDocument>.Filter.Eq("_id", projectId))
                    .FirstOrDefaultAsync(cancellationToken);
                var organizationId = project is null
                    ? null
                    : context.StringValue(project, "OrganizationId");
                if (string.IsNullOrWhiteSpace(projectId) || string.IsNullOrWhiteSpace(organizationId))
                {
                    throw new InvalidOperationException(
                        $"Work item '{workItemId}' cannot be migrated because project tenant ownership is missing.");
                }

                await UpsertWorkItemActivitiesAsync(
                    workItem,
                    organizationId,
                    projectId,
                    workItemId,
                    cancellationToken);

                var currentVersion = workItem.GetValue("Version", 0L).ToInt64();
                var versionFilter = workItem.Contains("Version")
                    ? Builders<BsonDocument>.Filter.Eq("Version", currentVersion)
                    : Builders<BsonDocument>.Filter.Exists("Version", false);
                var update = Builders<BsonDocument>.Update
                    .Set("ActivityStorageVersion", 1)
                    .Set("Comments", new BsonArray())
                    .Set("Attachments", new BsonArray())
                    .Set("WorkLogs", new BsonArray())
                    .Set("Approvals", new BsonArray())
                    .Set("StatusHistory", new BsonArray())
                    .Set("Version", checked(currentVersion + 1));
                var result = await workItems.UpdateOneAsync(
                    Builders<BsonDocument>.Filter.Eq("_id", workItem["_id"])
                    & versionFilter
                    & WorkItemActivityVersionFilter(),
                    update,
                    cancellationToken: cancellationToken);
                if (result.ModifiedCount == 1) ledger.Changed++; else ledger.Skipped++;
                await context.SaveOwnedLedgerAsync(ledger, cancellationToken);
            }

            ledger.Checkpoint = batch[^1]["_id"];
            ledger.State = MongoMigrationStates.Paused;
            await context.SaveOwnedLedgerAsync(ledger, cancellationToken);
        }

        await context.SaveAndReleaseOwnedLedgerAsync(ledger, cancellationToken);
        return context.ToOutcome(ledger, MongoMigrationStates.Paused);
    }

    internal static FilterDefinition<BsonDocument> WorkItemActivityFilter(BsonValue checkpoint)
    {
        var filter = WorkItemActivityVersionFilter();
        if (!checkpoint.IsBsonNull)
        {
            filter &= Builders<BsonDocument>.Filter.Gt("_id", checkpoint);
        }

        return filter;
    }

    internal static FilterDefinition<BsonDocument> WorkItemActivityVersionFilter() =>
        Builders<BsonDocument>.Filter.Or(
            Builders<BsonDocument>.Filter.Exists("ActivityStorageVersion", false),
            Builders<BsonDocument>.Filter.Lt("ActivityStorageVersion", 1));

    internal static bool HasMigratableActivities(
        BsonDocument workItem,
        Func<BsonDocument, string, BsonArray> arrayValue,
        Func<BsonDocument, string, string?> stringValue,
        Func<BsonValue, DateTime?> tryResolveUtc)
    {
        foreach (var field in new[] { "Comments", "Attachments", "WorkLogs", "Approvals" })
        {
            if (arrayValue(workItem, field).Any(value =>
                    value.IsBsonDocument
                    && !string.IsNullOrWhiteSpace(stringValue(value.AsBsonDocument, "Id"))))
            {
                return true;
            }
        }

        return arrayValue(workItem, "StatusHistory").Any(value =>
            value.IsBsonDocument
            && !string.IsNullOrWhiteSpace(stringValue(value.AsBsonDocument, "ToStatus"))
            && tryResolveUtc(value.AsBsonDocument.GetValue("ChangedAt", BsonNull.Value)) is not null);
    }

    internal async Task UpsertWorkItemActivitiesAsync(
        BsonDocument workItem,
        string organizationId,
        string projectId,
        string workItemId,
        CancellationToken cancellationToken)
    {
        var mongo = context.Mongo;
        var comments = mongo.GetCollection<BsonDocument>("workitemcommentactivitys", WorkItemsModule);
        var revisions = mongo.GetCollection<BsonDocument>(
            "workitemcommentrevisionactivitys",
            WorkItemsModule);
        var attachments = mongo.GetCollection<BsonDocument>(
            "workitemattachmentactivitys",
            WorkItemsModule);
        var workLogs = mongo.GetCollection<BsonDocument>("workitemworklogactivitys", WorkItemsModule);
        var approvals = mongo.GetCollection<BsonDocument>(
            "workitemapprovalactivitys",
            WorkItemsModule);
        var timeline = mongo.GetCollection<BsonDocument>("workitemtimelineactivitys", WorkItemsModule);

        foreach (var value in context.ArrayValue(workItem, "Comments"))
        {
            if (!value.IsBsonDocument) continue;
            var source = value.AsBsonDocument;
            var commentId = context.StringValue(source, "Id") ?? string.Empty;
            if (string.IsNullOrWhiteSpace(commentId)) continue;
            await ReplaceMigratedActivityAsync(comments, new BsonDocument
            {
                ["_id"] = commentId,
                ["OrganizationId"] = organizationId,
                ["ProjectId"] = projectId,
                ["WorkItemId"] = workItemId,
                ["Body"] = source.GetValue("Body", string.Empty),
                ["AuthorUserId"] = source.GetValue("AuthorUserId", "system"),
                ["Mentions"] = source.GetValue("Mentions", new BsonArray()),
                ["CreatedAt"] = source.GetValue("CreatedAt", BsonNull.Value),
                ["EditedAt"] = source.GetValue("EditedAt", BsonNull.Value),
                ["Version"] = 0L
            }, cancellationToken);

            var history = context.ArrayValue(source, "History");
            for (var ordinal = 0; ordinal < history.Count; ordinal++)
            {
                if (!history[ordinal].IsBsonDocument) continue;
                var revision = history[ordinal].AsBsonDocument;
                var editedAt = revision.GetValue("EditedAt", BsonNull.Value);
                var editedAtUtc = ResolveUtc(editedAt);
                if (editedAtUtc is null) continue;
                await ReplaceMigratedActivityAsync(revisions, new BsonDocument
                {
                    ["_id"] = ActivityId(
                        "revision",
                        workItemId,
                        commentId,
                        ordinal.ToString(),
                        editedAtUtc.Value.Ticks.ToString()),
                    ["OrganizationId"] = organizationId,
                    ["ProjectId"] = projectId,
                    ["WorkItemId"] = workItemId,
                    ["CommentId"] = commentId,
                    ["Body"] = revision.GetValue("Body", string.Empty),
                    ["EditedByUserId"] = revision.GetValue("EditedByUserId", "system"),
                    ["EditedAt"] = editedAt,
                    ["Version"] = 0L
                }, cancellationToken);
            }
        }

        await CopyArrayAsync(
            attachments,
            workItem,
            "Attachments",
            organizationId,
            projectId,
            workItemId,
            ["FileName", "ContentType", "SizeBytes", "StoragePath", "ChecksumSha256", "CreatedAt"],
            cancellationToken);
        await CopyArrayAsync(
            workLogs,
            workItem,
            "WorkLogs",
            organizationId,
            projectId,
            workItemId,
            ["UserId", "Hours", "Note", "CreatedAt"],
            cancellationToken);
        await CopyArrayAsync(
            approvals,
            workItem,
            "Approvals",
            organizationId,
            projectId,
            workItemId,
            [
                "FromStatus",
                "ToStatus",
                "RequestedByUserId",
                "RequestedAt",
                "ExpiresAt",
                "Status",
                "DecidedByUserId",
                "DecidedAt",
                "Note",
                "ConsumedAt"
            ],
            cancellationToken);

        var historyEntries = context.ArrayValue(workItem, "StatusHistory");
        for (var ordinal = 0; ordinal < historyEntries.Count; ordinal++)
        {
            if (!historyEntries[ordinal].IsBsonDocument) continue;
            var source = historyEntries[ordinal].AsBsonDocument;
            var changedAt = source.GetValue("ChangedAt", BsonNull.Value);
            var toStatus = context.StringValue(source, "ToStatus") ?? string.Empty;
            var changedAtUtc = ResolveUtc(changedAt);
            if (changedAtUtc is null || string.IsNullOrWhiteSpace(toStatus)) continue;
            await ReplaceMigratedActivityAsync(timeline, new BsonDocument
            {
                ["_id"] = ActivityId(
                    "timeline",
                    workItemId,
                    ordinal.ToString(),
                    changedAtUtc.Value.Ticks.ToString(),
                    toStatus),
                ["OrganizationId"] = organizationId,
                ["ProjectId"] = projectId,
                ["WorkItemId"] = workItemId,
                ["FromStatus"] = source.GetValue("FromStatus", BsonNull.Value),
                ["ToStatus"] = toStatus,
                ["ChangedByUserId"] = source.GetValue("ChangedByUserId", "system"),
                ["ChangedAt"] = changedAt,
                ["Version"] = 0L
            }, cancellationToken);
        }
    }

    internal static string ActivityId(params string[] parts)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\u001f', parts)));
        return Convert.ToHexString(bytes).ToLowerInvariant()[..32];
    }

    internal async Task CopyArrayAsync(
        IMongoCollection<BsonDocument> target,
        BsonDocument workItem,
        string field,
        string organizationId,
        string projectId,
        string workItemId,
        IReadOnlyCollection<string> copiedFields,
        CancellationToken cancellationToken)
    {
        foreach (var value in context.ArrayValue(workItem, field))
        {
            if (!value.IsBsonDocument) continue;
            var source = value.AsBsonDocument;
            var id = context.StringValue(source, "Id") ?? string.Empty;
            if (string.IsNullOrWhiteSpace(id)) continue;
            var activity = new BsonDocument
            {
                ["_id"] = id,
                ["OrganizationId"] = organizationId,
                ["ProjectId"] = projectId,
                ["WorkItemId"] = workItemId
            };
            foreach (var copiedField in copiedFields)
            {
                activity[copiedField] = source.GetValue(copiedField, BsonNull.Value);
            }
            activity["Version"] = 0L;
            await ReplaceMigratedActivityAsync(target, activity, cancellationToken);
        }
    }

    internal static async Task ReplaceMigratedActivityAsync(
        IMongoCollection<BsonDocument> collection,
        BsonDocument expected,
        CancellationToken cancellationToken)
    {
        var owner = Builders<BsonDocument>.Filter.Eq("_id", expected["_id"])
            & Builders<BsonDocument>.Filter.Eq("OrganizationId", expected["OrganizationId"])
            & Builders<BsonDocument>.Filter.Eq("ProjectId", expected["ProjectId"])
            & Builders<BsonDocument>.Filter.Eq("WorkItemId", expected["WorkItemId"]);
        try
        {
            await collection.ReplaceOneAsync(
                owner,
                expected,
                new ReplaceOptions { IsUpsert = true },
                cancellationToken);
        }
        catch (MongoWriteException exception)
            when (exception.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            throw new InvalidOperationException(
                $"Work item activity '{expected["_id"]}' conflicts with incompatible tenant ownership.",
                exception);
        }
    }

    private bool HasMigratableActivities(BsonDocument workItem) =>
        HasMigratableActivities(
            workItem,
            context.ArrayValue,
            context.StringValue,
            ResolveUtc);

    private DateTime? ResolveUtc(BsonValue value) =>
        context.TryResolveUtc(value, out var utc) ? utc : null;
}
