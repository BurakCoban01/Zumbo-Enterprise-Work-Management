using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using Zumbo.BuildingBlocks.Infrastructure.Persistence;

public sealed partial class MongoMigrationRunner{

    private async Task<MongoMigrationOutcome> BackfillWorkItemActivitiesAsync(CancellationToken cancellationToken)
    {
        var existing = await LoadLedgerAsync(WorkItemActivityMigrationId, cancellationToken);
        if (existing is not null)
        {
            EnsureChecksum(existing, WorkItemActivityChecksum);
            if (existing.State == MongoMigrationStates.Completed)
            {
                return ToOutcome(existing, MongoMigrationStates.Skipped);
            }
        }

        var workItems = mongo.GetCollection<BsonDocument>("workitems", WorkItemsModule);
        if (_options.DryRun)
        {
            var count = await workItems.CountDocumentsAsync(
                WorkItemActivityFilter(BsonNull.Value),
                cancellationToken: cancellationToken);
            return new MongoMigrationOutcome(WorkItemActivityMigrationId, MongoMigrationStates.DryRun, count);
        }

        var ledger = await GetOrCreateLedgerAsync(
            WorkItemActivityMigrationId,
            WorkItemActivityChecksum,
            cancellationToken);
        ledger = await AcquireLeaseAsync(ledger, cancellationToken);
        if (ledger.LeaseOwner != _owner)
        {
            return ToOutcome(ledger, MongoMigrationStates.Busy);
        }

        var projects = mongo.GetCollection<BsonDocument>("projects", "Projects");
        for (var batchNumber = 0; batchNumber < MaxBatches; batchNumber++)
        {
            var batch = await workItems.Find(WorkItemActivityFilter(ledger.Checkpoint))
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
                var workItemId = workItem["_id"].ToString() ?? string.Empty;
                var projectId = StringValue(workItem, "ProjectId") ?? string.Empty;
                if (string.IsNullOrWhiteSpace(workItemId))
                {
                    throw new InvalidOperationException(
                        "A work item with an empty identifier cannot be migrated.");
                }

                if (!HasMigratableActivities(workItem))
                {
                    ledger.Skipped++;
                    await SaveOwnedLedgerAsync(ledger, cancellationToken);
                    continue;
                }

                var project = await projects.Find(Builders<BsonDocument>.Filter.Eq("_id", projectId))
                    .FirstOrDefaultAsync(cancellationToken);
                var organizationId = project is null ? null : StringValue(project, "OrganizationId");
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
                await SaveOwnedLedgerAsync(ledger, cancellationToken);
            }

            ledger.Checkpoint = batch[^1]["_id"];
            ledger.State = MongoMigrationStates.Paused;
            await SaveOwnedLedgerAsync(ledger, cancellationToken);
        }

        await SaveAndReleaseOwnedLedgerAsync(ledger, cancellationToken);
        return ToOutcome(ledger, MongoMigrationStates.Paused);
    }
}
