using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using Zumbo.BuildingBlocks.Infrastructure.Persistence;

public sealed partial class MongoMigrationRunner{

    public async Task<MongoMigrationOutcome> RollbackAsync(
        string migrationId,
        CancellationToken cancellationToken = default)
    {
        if (!string.Equals(migrationId, RankMigrationId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Migration '{migrationId}' does not support rollback.");
        }

        if (_options.DryRun)
        {
            return new MongoMigrationOutcome(migrationId, MongoMigrationStates.DryRun);
        }

        var ledger = await LoadLedgerAsync(migrationId, cancellationToken)
            ?? throw new InvalidOperationException($"Migration '{migrationId}' has not been applied.");
        EnsureChecksum(ledger, RankChecksum);
        if (ledger.State == MongoMigrationStates.RolledBack)
        {
            return ToOutcome(ledger, MongoMigrationStates.Skipped);
        }

        ledger = await AcquireLeaseAsync(ledger, cancellationToken);
        if (ledger.LeaseOwner != _owner)
        {
            return ToOutcome(ledger, MongoMigrationStates.Busy);
        }

        var backups = mongo.GetCollection<MongoRankMigrationBackupDocument>(BackupCollection, WorkItemsModule);
        var workItems = mongo.GetCollection<BsonDocument>("workitems", WorkItemsModule);
        var changed = 0L;
        var skipped = 0L;
        while (true)
        {
            var filter = Builders<MongoRankMigrationBackupDocument>.Filter.Eq(x => x.MigrationId, migrationId);
            if (!ledger.RollbackCheckpoint.IsBsonNull)
            {
                filter &= Builders<MongoRankMigrationBackupDocument>.Filter.Gt(x => x.DocumentId, ledger.RollbackCheckpoint);
            }

            var batch = await backups.Find(filter)
                .SortBy(x => x.DocumentId)
                .Limit(BatchSize)
                .ToListAsync(cancellationToken);
            if (batch.Count == 0)
            {
                break;
            }

            foreach (var backup in batch)
            {
                var currentFilter = new BsonDocument
                {
                    ["_id"] = backup.DocumentId,
                    ["Rank"] = backup.AppliedRank
                };
                var rankUpdate = backup.HadRank
                    ? new BsonDocument("$set", new BsonDocument("Rank", backup.PreviousRank))
                    : new BsonDocument("$unset", new BsonDocument("Rank", string.Empty));
                var result = await workItems.UpdateOneAsync(currentFilter, rankUpdate, cancellationToken: cancellationToken);
                if (result.ModifiedCount == 1) changed++; else skipped++;
            }

            ledger.RollbackCheckpoint = batch[^1].DocumentId;
            ledger.Changed = changed;
            ledger.Skipped = skipped;
            await SaveOwnedLedgerAsync(ledger, cancellationToken);
        }

        ledger.State = MongoMigrationStates.RolledBack;
        ledger.Checkpoint = BsonNull.Value;
        ledger.RollbackCheckpoint = BsonNull.Value;
        ledger.RolledBackAt = DateTime.UtcNow;
        await SaveAndReleaseOwnedLedgerAsync(ledger, cancellationToken);
        return ToOutcome(ledger, MongoMigrationStates.RolledBack);
    }

    private static string BackupId(BsonValue id) => $"{RankMigrationId}:{id.BsonType}:{id}";
}
