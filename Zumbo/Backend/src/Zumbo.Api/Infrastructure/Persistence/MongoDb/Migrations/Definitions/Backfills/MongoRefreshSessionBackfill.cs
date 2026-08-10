using MongoDB.Bson;
using MongoDB.Driver;

using Zumbo.Api.Infrastructure.Persistence.MongoDb.Migrations.Abstractions;

namespace Zumbo.Api.Infrastructure.Persistence.MongoDb.Migrations.Definitions.Backfills;

internal sealed class MongoRefreshSessionBackfill(
    IMongoMigrationExecutionContext context,
    string migrationId,
    string checksum)
{
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
        var users = mongo.GetCollection<BsonDocument>("users", "Identity");
        if (context.Options.DryRun)
        {
            var count = await users.CountDocumentsAsync(
                RefreshSessionUserFilter(BsonNull.Value),
                cancellationToken: cancellationToken);
            return new MongoMigrationOutcome(migrationId, MongoMigrationStates.DryRun, count);
        }

        var sessions = mongo.GetCollection<BsonDocument>("refreshsessions", "Identity");
        var ledger = await context.GetOrCreateLedgerAsync(migrationId, checksum, cancellationToken);
        ledger = await context.AcquireLeaseAsync(ledger, cancellationToken);
        if (ledger.LeaseOwner != context.Owner)
        {
            return context.ToOutcome(ledger, MongoMigrationStates.Busy);
        }

        for (var batchNumber = 0; batchNumber < context.MaxBatches; batchNumber++)
        {
            var batch = await users.Find(RefreshSessionUserFilter(ledger.Checkpoint))
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

            foreach (var user in batch)
            {
                ledger.Examined++;
                var userId = user["_id"].ToString() ?? string.Empty;
                var organizationId = context.StringValue(user, "OrganizationId") ?? string.Empty;
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

                await context.SaveOwnedLedgerAsync(ledger, cancellationToken);
            }

            ledger.Checkpoint = batch[^1]["_id"];
            ledger.State = MongoMigrationStates.Paused;
            await context.SaveOwnedLedgerAsync(ledger, cancellationToken);
        }

        await context.SaveAndReleaseOwnedLedgerAsync(ledger, cancellationToken);
        return context.ToOutcome(ledger, MongoMigrationStates.Paused);
    }

    internal static FilterDefinition<BsonDocument> RefreshSessionUserFilter(BsonValue checkpoint)
    {
        var filter = Builders<BsonDocument>.Filter.Exists("RefreshTokens.0", true);
        if (!checkpoint.IsBsonNull)
        {
            filter &= Builders<BsonDocument>.Filter.Gt("_id", checkpoint);
        }

        return filter;
    }

    internal static bool TryCreateRefreshSession(
        BsonValue value,
        string userId,
        string organizationId,
        Func<BsonDocument, string, string?> stringValue,
        Func<BsonValue, DateTime?> tryResolveUtc,
        out BsonDocument session)
    {
        session = null!;
        if (!value.IsBsonDocument
            || string.IsNullOrWhiteSpace(userId)
            || string.IsNullOrWhiteSpace(organizationId))
        {
            return false;
        }

        var token = value.AsBsonDocument;
        var sessionId = stringValue(token, "SessionId");
        var tokenHash = stringValue(token, "TokenHash");
        var expiresAt = tryResolveUtc(token.GetValue("ExpiresAt", BsonNull.Value));
        if (string.IsNullOrWhiteSpace(sessionId)
            || string.IsNullOrWhiteSpace(tokenHash)
            || expiresAt is null)
        {
            return false;
        }

        var createdAt = token.GetValue("CreatedAt", token["ExpiresAt"]);
        var revokedValue = token.GetValue("RevokedAt", BsonNull.Value);
        BsonValue revokedAtUtc = BsonNull.Value;
        var retainBase = expiresAt.Value;
        var revokedAt = tryResolveUtc(revokedValue);
        if (revokedAt is not null)
        {
            revokedAtUtc = revokedAt.Value;
            if (revokedAt.Value > retainBase)
            {
                retainBase = revokedAt.Value;
            }
        }

        session = new BsonDocument
        {
            ["_id"] = sessionId,
            ["UserId"] = userId,
            ["OrganizationId"] = organizationId,
            ["TokenHash"] = tokenHash,
            ["CreatedAt"] = createdAt,
            ["ExpiresAt"] = token["ExpiresAt"],
            ["ExpiresAtUtc"] = expiresAt.Value,
            ["RevokedAt"] = revokedValue,
            ["RevokedAtUtc"] = revokedAtUtc,
            ["ReplacedBySessionId"] = BsonNull.Value,
            ["RetainUntilUtc"] = retainBase.AddDays(30),
            ["Version"] = 1L
        };
        return true;
    }

    internal static async Task EnsureRefreshSessionMatchesAsync(
        IMongoCollection<BsonDocument> sessions,
        BsonDocument expected,
        CancellationToken cancellationToken)
    {
        var filter = Builders<BsonDocument>.Filter.Or(
            Builders<BsonDocument>.Filter.Eq("_id", expected["_id"]),
            Builders<BsonDocument>.Filter.Eq("TokenHash", expected["TokenHash"]));
        var actual = await sessions.Find(filter).FirstOrDefaultAsync(cancellationToken);
        if (actual is null
            || actual.GetValue("_id", BsonNull.Value) != expected["_id"]
            || actual.GetValue("UserId", BsonNull.Value) != expected["UserId"]
            || actual.GetValue("OrganizationId", BsonNull.Value) != expected["OrganizationId"]
            || actual.GetValue("TokenHash", BsonNull.Value) != expected["TokenHash"])
        {
            throw new InvalidOperationException(
                $"Refresh session '{expected["_id"]}' conflicts with incompatible stored ownership or token data.");
        }
    }

    private bool TryCreateRefreshSession(
        BsonValue value,
        string userId,
        string organizationId,
        out BsonDocument session) =>
        TryCreateRefreshSession(
            value,
            userId,
            organizationId,
            context.StringValue,
            ResolveUtc,
            out session);

    private DateTime? ResolveUtc(BsonValue value) =>
        context.TryResolveUtc(value, out var utc) ? utc : null;
}
