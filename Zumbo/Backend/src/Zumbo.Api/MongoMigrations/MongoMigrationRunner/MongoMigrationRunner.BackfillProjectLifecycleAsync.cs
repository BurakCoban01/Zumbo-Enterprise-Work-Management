using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using Zumbo.BuildingBlocks.Infrastructure.Persistence;

public sealed partial class MongoMigrationRunner{

    private async Task<MongoMigrationOutcome> BackfillProjectLifecycleAsync(
        CancellationToken cancellationToken)
    {
        var existing = await LoadLedgerAsync(ProjectLifecycleMigrationId, cancellationToken);
        if (existing is not null)
        {
            EnsureChecksum(existing, ProjectLifecycleChecksum);
            if (existing.State == MongoMigrationStates.Completed)
            {
                return ToOutcome(existing, MongoMigrationStates.Skipped);
            }
        }

        var projects = mongo.GetCollection<BsonDocument>("projects", "Projects");
        if (_options.DryRun)
        {
            var count = await projects.CountDocumentsAsync(
                ProjectLifecycleFilter(BsonNull.Value),
                cancellationToken: cancellationToken);
            return new MongoMigrationOutcome(ProjectLifecycleMigrationId, MongoMigrationStates.DryRun, count);
        }

        var ledger = await GetOrCreateLedgerAsync(
            ProjectLifecycleMigrationId,
            ProjectLifecycleChecksum,
            cancellationToken);
        ledger = await AcquireLeaseAsync(ledger, cancellationToken);
        if (ledger.LeaseOwner != _owner)
        {
            return ToOutcome(ledger, MongoMigrationStates.Busy);
        }

        for (var batchNumber = 0; batchNumber < MaxBatches; batchNumber++)
        {
            var batch = await projects.Find(ProjectLifecycleFilter(ledger.Checkpoint))
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
                var updates = new List<UpdateDefinition<BsonDocument>>
                {
                    Builders<BsonDocument>.Update.Set("Version", version + 1),
                    Builders<BsonDocument>.Update.Set("ProjectLifecycleMigratedBy", ProjectLifecycleMigrationId)
                };
                AddProjectDefault(document, updates, "Visibility", "Internal");
                AddProjectDefault(document, updates, "Archived", false);
                AddProjectDefault(document, updates, "Members", new BsonArray());
                AddProjectDefault(document, updates, "TeamIds", new BsonArray());
                AddProjectDefault(document, updates, "Templates", new BsonArray());
                AddProjectDefault(document, updates, "Components", new BsonArray());
                AddProjectDefault(document, updates, "Versions", new BsonArray());
                AddProjectDefault(document, updates, "Releases", new BsonArray());
                AddProjectDefault(document, updates, "Milestones", new BsonArray());
                AddProjectDefault(document, updates, "ArchivedAt", BsonNull.Value);
                AddProjectDefault(document, updates, "RetainUntil", BsonNull.Value);

                var result = await projects.UpdateOneAsync(
                    ProjectVersionForId(document["_id"], version),
                    Builders<BsonDocument>.Update.Combine(updates),
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
