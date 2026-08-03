using MongoDB.Bson;
using MongoDB.Driver;

public sealed partial class MongoMigrationRunner
{
    private async Task<MongoMigrationOutcome> CleanupLegacyMigrationMarkersAsync(
        CancellationToken cancellationToken)
    {
        var existing = await LoadLedgerAsync(LegacyMigrationMarkerCleanupId, cancellationToken);
        if (existing is not null)
        {
            EnsureChecksum(existing, LegacyMigrationMarkerCleanupChecksum);
            if (existing.State == MongoMigrationStates.Completed)
            {
                return ToOutcome(existing, MongoMigrationStates.Skipped);
            }
        }

        var targets = new (string Module, string Collection, string[] Markers)[]
        {
            ("Projects", "projects", ["ProjectLifecycleMigratedBy"]),
            ("Workflows", "workflows", ["WorkflowLifecycleMigratedBy"]),
            ("WorkItems", "workitems", ["SprintLifecycleMigratedBy", "WorkItemTypeSchemaMigratedBy"]),
            ("Teams", "teams", ["TeamInviteTokenMigratedBy"])
        };

        if (_options.DryRun)
        {
            long count = 0;
            foreach (var target in targets)
            {
                var collection = mongo.GetCollection<BsonDocument>(target.Collection, target.Module);
                count += await collection.CountDocumentsAsync(
                    MarkerFilter(target.Markers),
                    cancellationToken: cancellationToken);
            }

            return new MongoMigrationOutcome(
                LegacyMigrationMarkerCleanupId,
                MongoMigrationStates.DryRun,
                count);
        }

        var ledger = await GetOrCreateLedgerAsync(
            LegacyMigrationMarkerCleanupId,
            LegacyMigrationMarkerCleanupChecksum,
            cancellationToken);
        ledger = await AcquireLeaseAsync(ledger, cancellationToken);
        if (ledger.LeaseOwner != _owner)
        {
            return ToOutcome(ledger, MongoMigrationStates.Busy);
        }

        foreach (var target in targets)
        {
            var collection = mongo.GetCollection<BsonDocument>(target.Collection, target.Module);
            var result = await collection.UpdateManyAsync(
                MarkerFilter(target.Markers),
                Builders<BsonDocument>.Update.Combine(target.Markers.Select(marker =>
                    Builders<BsonDocument>.Update.Unset(marker))),
                cancellationToken: cancellationToken);
            ledger.Examined += result.MatchedCount;
            ledger.Changed += result.ModifiedCount;
            await SaveOwnedLedgerAsync(ledger, cancellationToken);
        }

        ledger.State = MongoMigrationStates.Completed;
        ledger.CompletedAt = DateTime.UtcNow;
        await SaveAndReleaseOwnedLedgerAsync(ledger, cancellationToken);
        return ToOutcome(ledger, MongoMigrationStates.Completed);
    }

    private static FilterDefinition<BsonDocument> MarkerFilter(IEnumerable<string> markers) =>
        Builders<BsonDocument>.Filter.Or(markers.Select(marker =>
            Builders<BsonDocument>.Filter.Exists(marker)));
}
