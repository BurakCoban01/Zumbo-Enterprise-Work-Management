using MongoDB.Bson;
using MongoDB.Driver;

using Zumbo.Api.Infrastructure.Persistence.MongoDb.Migrations.Abstractions;

namespace Zumbo.Api.Infrastructure.Persistence.MongoDb.Migrations.Definitions.Backfills;

internal sealed class MongoRankBackfill(
    IMongoMigrationExecutionContext context,
    string migrationId,
    string checksum)
{
    private const string BackupCollection = "__zumbo_migration_rank_backups";
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
                RankCandidateFilter(BsonNull.Value),
                cancellationToken: cancellationToken);
            return new MongoMigrationOutcome(migrationId, MongoMigrationStates.DryRun, count);
        }

        var ledger = await context.GetOrCreateLedgerAsync(migrationId, checksum, cancellationToken);
        if (ledger.State == MongoMigrationStates.RolledBack)
        {
            ledger.State = MongoMigrationStates.Running;
            ledger.Examined = 0;
            ledger.Changed = 0;
            ledger.Skipped = 0;
            ledger.CompletedAt = null;
            ledger.RolledBackAt = null;
            await context.SaveLedgerAsync(ledger, cancellationToken);
        }

        ledger = await context.AcquireLeaseAsync(ledger, cancellationToken);
        if (ledger.LeaseOwner != context.Owner)
        {
            return context.ToOutcome(ledger, MongoMigrationStates.Busy);
        }

        var backups = mongo.GetCollection<MongoRankMigrationBackupDocument>(
            BackupCollection,
            WorkItemsModule);
        for (var batchNumber = 0; batchNumber < context.MaxBatches; batchNumber++)
        {
            var batch = await workItems.Find(RankCandidateFilter(ledger.Checkpoint))
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

            foreach (var document in batch)
            {
                ledger.Examined++;
                var id = document["_id"];
                if (!TryResolveRank(
                        document.GetValue("CreatedAt", BsonNull.Value),
                        context.NumericTicks,
                        out var rank))
                {
                    ledger.Skipped++;
                    continue;
                }

                var hadRank = document.TryGetValue("Rank", out var previousRank);
                var backup = new MongoRankMigrationBackupDocument
                {
                    Id = BackupId(migrationId, id),
                    MigrationId = migrationId,
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
            await context.SaveOwnedLedgerAsync(ledger, cancellationToken);
        }

        await context.SaveAndReleaseOwnedLedgerAsync(ledger, cancellationToken);
        return context.ToOutcome(ledger, MongoMigrationStates.Paused);
    }

    internal static FilterDefinition<BsonDocument> RankCandidateFilter(BsonValue checkpoint)
    {
        var filter = Builders<BsonDocument>.Filter.Or(
            Builders<BsonDocument>.Filter.Exists("Rank", false),
            Builders<BsonDocument>.Filter.Eq("Rank", 0));
        if (!checkpoint.IsBsonNull)
        {
            filter &= Builders<BsonDocument>.Filter.Gt("_id", checkpoint);
        }

        return filter;
    }

    internal static FilterDefinition<BsonDocument> RankCandidateForId(BsonValue id) =>
        Builders<BsonDocument>.Filter.Eq("_id", id)
        & Builders<BsonDocument>.Filter.Or(
            Builders<BsonDocument>.Filter.Exists("Rank", false),
            Builders<BsonDocument>.Filter.Eq("Rank", 0));

    internal static bool TryResolveRank(
        BsonValue createdAt,
        Func<BsonValue, long> numericTicks,
        out long rank)
    {
        rank = 0;
        try
        {
            rank = createdAt.BsonType switch
            {
                BsonType.DateTime => DateTimeOffset.FromUnixTimeMilliseconds(
                    createdAt.AsBsonDateTime.MillisecondsSinceEpoch).UtcTicks,
                BsonType.Int64 => createdAt.AsInt64,
                BsonType.Int32 => createdAt.AsInt32,
                BsonType.Array when createdAt.AsBsonArray.Count > 0 =>
                    numericTicks(createdAt.AsBsonArray[0]),
                BsonType.Document => ResolveDocumentTicks(createdAt.AsBsonDocument, numericTicks),
                _ => 0
            };
        }
        catch (Exception exception)
            when (exception is InvalidOperationException or ArgumentOutOfRangeException or FormatException)
        {
            rank = 0;
        }

        return rank > 0 && rank <= DateTimeOffset.MaxValue.UtcTicks;
    }

    internal static long ResolveDocumentTicks(
        BsonDocument document,
        Func<BsonValue, long> numericTicks)
    {
        if (document.TryGetValue("Ticks", out var ticks)) return numericTicks(ticks);
        if (document.TryGetValue("DateTime", out var dateTime) && dateTime.IsBsonDateTime)
        {
            return DateTimeOffset.FromUnixTimeMilliseconds(
                dateTime.AsBsonDateTime.MillisecondsSinceEpoch).UtcTicks;
        }

        return 0;
    }

    private static string BackupId(string migrationId, BsonValue id) =>
        $"{migrationId}:{id.BsonType}:{id}";
}
