using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using Zumbo.BuildingBlocks.Infrastructure.Persistence;

public sealed partial class MongoMigrationRunner{

    private async Task<MongoMigrationOutcome> BackfillRefreshSessionsAsync(CancellationToken cancellationToken)
    {
        var existing = await LoadLedgerAsync(RefreshSessionMigrationId, cancellationToken);
        if (existing is not null)
        {
            EnsureChecksum(existing, RefreshSessionChecksum);
            if (existing.State == MongoMigrationStates.Completed)
            {
                return ToOutcome(existing, MongoMigrationStates.Skipped);
            }
        }

        var users = mongo.GetCollection<BsonDocument>("users", "Identity");
        if (_options.DryRun)
        {
            var count = await users.CountDocumentsAsync(
                RefreshSessionUserFilter(BsonNull.Value),
                cancellationToken: cancellationToken);
            return new MongoMigrationOutcome(RefreshSessionMigrationId, MongoMigrationStates.DryRun, count);
        }

        var sessions = mongo.GetCollection<BsonDocument>("refreshsessions", "Identity");
        var ledger = await GetOrCreateLedgerAsync(
            RefreshSessionMigrationId,
            RefreshSessionChecksum,
            cancellationToken);
        ledger = await AcquireLeaseAsync(ledger, cancellationToken);
        if (ledger.LeaseOwner != _owner)
        {
            return ToOutcome(ledger, MongoMigrationStates.Busy);
        }

        for (var batchNumber = 0; batchNumber < MaxBatches; batchNumber++)
        {
            var batch = await users.Find(RefreshSessionUserFilter(ledger.Checkpoint))
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

            foreach (var user in batch)
            {
                ledger.Examined++;
                var userId = user["_id"].ToString() ?? string.Empty;
                var organizationId = StringValue(user, "OrganizationId") ?? string.Empty;
                foreach (var value in user["RefreshTokens"].AsBsonArray)
                {
                    if (!TryCreateRefreshSession(value, userId, organizationId, out var session))
                    {
                        ledger.Skipped++;
                        continue;
                    }

                    try
                    {
                        var result = await sessions.UpdateOneAsync(
                            Builders<BsonDocument>.Filter.Eq("_id", session["_id"]),
                            new BsonDocument("$setOnInsert", session),
                            new UpdateOptions { IsUpsert = true },
                            cancellationToken);
                        if (result.UpsertedId is null)
                        {
                            await EnsureRefreshSessionMatchesAsync(sessions, session, cancellationToken);
                            ledger.Skipped++;
                        }
                        else
                        {
                            ledger.Changed++;
                        }
                    }
                    catch (MongoWriteException exception)
                        when (exception.WriteError?.Category == ServerErrorCategory.DuplicateKey)
                    {
                        await EnsureRefreshSessionMatchesAsync(sessions, session, cancellationToken);
                        ledger.Skipped++;
                    }
                }

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
