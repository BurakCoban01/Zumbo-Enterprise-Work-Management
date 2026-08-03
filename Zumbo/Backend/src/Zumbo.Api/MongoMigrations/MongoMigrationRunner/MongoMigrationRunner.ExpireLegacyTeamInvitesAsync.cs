using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using Zumbo.BuildingBlocks.Infrastructure.Persistence;

public sealed partial class MongoMigrationRunner{

    private async Task<MongoMigrationOutcome> ExpireLegacyTeamInvitesAsync(
        CancellationToken cancellationToken)
    {
        var existing = await LoadLedgerAsync(TeamInviteTokenMigrationId, cancellationToken);
        if (existing is not null)
        {
            EnsureChecksum(existing, TeamInviteTokenChecksum);
            if (existing.State == MongoMigrationStates.Completed)
            {
                return ToOutcome(existing, MongoMigrationStates.Skipped);
            }
        }

        var teams = mongo.GetCollection<BsonDocument>("teams", "Teams");
        if (_options.DryRun)
        {
            var count = await teams.CountDocumentsAsync(
                LegacyTeamInviteFilter(BsonNull.Value),
                cancellationToken: cancellationToken);
            return new MongoMigrationOutcome(
                TeamInviteTokenMigrationId,
                MongoMigrationStates.DryRun,
                count);
        }

        var ledger = await GetOrCreateLedgerAsync(
            TeamInviteTokenMigrationId,
            TeamInviteTokenChecksum,
            cancellationToken);
        ledger = await AcquireLeaseAsync(ledger, cancellationToken);
        if (ledger.LeaseOwner != _owner)
        {
            return ToOutcome(ledger, MongoMigrationStates.Busy);
        }

        for (var batchNumber = 0; batchNumber < MaxBatches; batchNumber++)
        {
            var batch = await teams.Find(LegacyTeamInviteFilter(ledger.Checkpoint))
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
                    document["TeamInviteTokenMigratedBy"] = TeamInviteTokenMigrationId;
                    var result = await teams.ReplaceOneAsync(
                        TeamVersionForId(document["_id"], version),
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
            await SaveOwnedLedgerAsync(ledger, cancellationToken);
        }

        await SaveAndReleaseOwnedLedgerAsync(ledger, cancellationToken);
        return ToOutcome(ledger, MongoMigrationStates.Paused);
    }
}
