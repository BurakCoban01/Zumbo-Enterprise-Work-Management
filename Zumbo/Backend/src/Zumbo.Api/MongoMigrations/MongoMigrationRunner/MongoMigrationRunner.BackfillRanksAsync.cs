using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using Zumbo.BuildingBlocks.Infrastructure.Persistence;

public sealed partial class MongoMigrationRunner{

    private async Task<MongoMigrationOutcome> BackfillRanksAsync(CancellationToken cancellationToken)
    {
        var existing = await LoadLedgerAsync(RankMigrationId, cancellationToken);
        if (existing is not null)
        {
            EnsureChecksum(existing, RankChecksum);
            if (existing.State == MongoMigrationStates.Completed)
            {
                return ToOutcome(existing, MongoMigrationStates.Skipped);
            }
        }

        var workItems = mongo.GetCollection<BsonDocument>("workitems", WorkItemsModule);
        if (_options.DryRun)
        {
            var count = await workItems.CountDocumentsAsync(RankCandidateFilter(BsonNull.Value), cancellationToken: cancellationToken);
            return new MongoMigrationOutcome(RankMigrationId, MongoMigrationStates.DryRun, count);
        }

        var ledger = await GetOrCreateLedgerAsync(RankMigrationId, RankChecksum, cancellationToken);
        if (ledger.State == MongoMigrationStates.RolledBack)
        {
            ledger.State = MongoMigrationStates.Running;
            ledger.Examined = 0;
            ledger.Changed = 0;
            ledger.Skipped = 0;
            ledger.CompletedAt = null;
            ledger.RolledBackAt = null;
            await SaveLedgerAsync(ledger, cancellationToken);
        }

        ledger = await AcquireLeaseAsync(ledger, cancellationToken);
        if (ledger.LeaseOwner != _owner)
        {
            return ToOutcome(ledger, MongoMigrationStates.Busy);
        }

        var backups = mongo.GetCollection<MongoRankMigrationBackupDocument>(BackupCollection, WorkItemsModule);
        for (var batchNumber = 0; batchNumber < MaxBatches; batchNumber++)
        {
            var batch = await workItems.Find(RankCandidateFilter(ledger.Checkpoint))
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
                var id = document["_id"];
                if (!TryResolveRank(document.GetValue("CreatedAt", BsonNull.Value), out var rank))
                {
                    ledger.Skipped++;
                    continue;
                }

                var hadRank = document.TryGetValue("Rank", out var previousRank);
                var backup = new MongoRankMigrationBackupDocument
                {
                    Id = BackupId(id),
                    MigrationId = RankMigrationId,
                    DocumentId = id,
                    HadRank = hadRank,
                    PreviousRank = hadRank ? previousRank! : BsonNull.Value,
                    AppliedRank = rank
                };
                await backups.ReplaceOneAsync(
                    x => x.Id == backup.Id,
                    backup,
                    new ReplaceOptions { IsUpsert = true },
                    cancellationToken);

                var result = await workItems.UpdateOneAsync(
                    RankCandidateForId(id),
                    new BsonDocument("$set", new BsonDocument("Rank", rank)),
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
