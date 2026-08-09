using MongoDB.Bson;
using MongoDB.Driver;

internal sealed class MongoTeamInviteBackfill(
    IMongoMigrationExecutionContext context,
    string migrationId,
    string checksum)
{
    public async Task<MongoMigrationOutcome> ExecuteAsync(CancellationToken cancellationToken)
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
        var teams = mongo.GetCollection<BsonDocument>("teams", "Teams");
        if (context.Options.DryRun)
        {
            var count = await teams.CountDocumentsAsync(Filter(BsonNull.Value), cancellationToken: cancellationToken);
            return new MongoMigrationOutcome(migrationId, MongoMigrationStates.DryRun, count);
        }

        var ledger = await context.GetOrCreateLedgerAsync(migrationId, checksum, cancellationToken);
        ledger = await context.AcquireLeaseAsync(ledger, cancellationToken);
        if (ledger.LeaseOwner != context.Owner)
        {
            return context.ToOutcome(ledger, MongoMigrationStates.Busy);
        }

        for (var batchNumber = 0; batchNumber < context.MaxBatches; batchNumber++)
        {
            var batch = await teams.Find(Filter(ledger.Checkpoint))
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
                var version = Math.Max(context.NumericTicks(document.GetValue("Version", 0)), 0);
                var changed = false;
                foreach (var memberValue in document.GetValue("Members", new BsonArray()).AsBsonArray)
                {
                    if (!memberValue.IsBsonDocument)
                    {
                        continue;
                    }

                    var member = memberValue.AsBsonDocument;
                    var tokenHash = member.GetValue("InvitationTokenHash", BsonNull.Value);
                    if (member.GetValue("Status", string.Empty) != "Invited"
                        || (tokenHash.IsString && !string.IsNullOrWhiteSpace(tokenHash.AsString)))
                    {
                        continue;
                    }

                    member["Status"] = "Expired";
                    member["InvitationTokenHash"] = BsonNull.Value;
                    member["InvitationExpiresAt"] = BsonNull.Value;
                    member["RespondedAt"] = DateTime.UtcNow;
                    changed = true;
                }

                if (changed)
                {
                    document["Version"] = version + 1;
                    document["TeamInviteTokenMigratedBy"] = migrationId;
                    var result = await teams.ReplaceOneAsync(
                        ForId(document["_id"], version),
                        document,
                        cancellationToken: cancellationToken);
                    if (result.ModifiedCount == 1) ledger.Changed++; else ledger.Skipped++;
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

    internal static FilterDefinition<BsonDocument> Filter(BsonValue checkpoint)
    {
        var pendingWithoutHash = Builders<BsonDocument>.Filter.ElemMatch(
            "Members",
            Builders<BsonDocument>.Filter.Eq("Status", "Invited")
            & Builders<BsonDocument>.Filter.Or(
                Builders<BsonDocument>.Filter.Exists("InvitationTokenHash", false),
                Builders<BsonDocument>.Filter.Eq("InvitationTokenHash", BsonNull.Value),
                Builders<BsonDocument>.Filter.Eq("InvitationTokenHash", string.Empty)));
        if (!checkpoint.IsBsonNull)
        {
            pendingWithoutHash &= Builders<BsonDocument>.Filter.Gt("_id", checkpoint);
        }

        return pendingWithoutHash;
    }

    internal static FilterDefinition<BsonDocument> ForId(BsonValue id, long version)
    {
        var versionFilter = version == 0
            ? Builders<BsonDocument>.Filter.Or(
                Builders<BsonDocument>.Filter.Exists("Version", false),
                Builders<BsonDocument>.Filter.Eq("Version", 0),
                Builders<BsonDocument>.Filter.Type("Version", BsonType.Null))
            : Builders<BsonDocument>.Filter.Eq("Version", version);
        return Builders<BsonDocument>.Filter.Eq("_id", id) & versionFilter;
    }
}
