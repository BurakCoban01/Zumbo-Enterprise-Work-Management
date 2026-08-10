using MongoDB.Bson;
using MongoDB.Driver;

using Zumbo.Api.Infrastructure.Persistence.MongoDb.Migrations.Abstractions;

namespace Zumbo.Api.Infrastructure.Persistence.MongoDb.Migrations.Definitions.Backfills;

internal sealed class MongoLegacyMarkerCleanup(
    IMongoMigrationExecutionContext context,
    string migrationId,
    string checksum)
{
    internal async Task<MongoMigrationOutcome> ExecuteAsync(CancellationToken cancellationToken)
    {
        var mongo = context.Mongo;
        var existing = await context.LoadLedgerAsync(migrationId, cancellationToken);
        if (existing is not null)
        {
            context.EnsureChecksum(existing, checksum);
            if (existing.State == MongoMigrationStates.Completed)
            {
                return context.ToOutcome(existing, MongoMigrationStates.Skipped);
            }
        }

        var targets = new (string Module, string Collection, string[] Markers)[]
        {
            ("Projects", "projects", ["ProjectLifecycleMigratedBy"]),
            ("Workflows", "workflows", ["WorkflowLifecycleMigratedBy"]),
            ("WorkItems", "workitems", ["SprintLifecycleMigratedBy", "WorkItemTypeSchemaMigratedBy"]),
            ("Teams", "teams", ["TeamInviteTokenMigratedBy"])
        };

        if (context.Options.DryRun)
        {
            long count = 0;
            foreach (var target in targets)
            {
                var collection = mongo.GetCollection<BsonDocument>(target.Collection, target.Module);
                count += await collection.CountDocumentsAsync(
                    MarkerFilter(target.Markers),
                    cancellationToken: cancellationToken);
            }

            return new MongoMigrationOutcome(migrationId, MongoMigrationStates.DryRun, count);
        }

        var ledger = await context.GetOrCreateLedgerAsync(migrationId, checksum, cancellationToken);
        ledger = await context.AcquireLeaseAsync(ledger, cancellationToken);
        if (ledger.LeaseOwner != context.Owner)
        {
            return context.ToOutcome(ledger, MongoMigrationStates.Busy);
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
            await context.SaveOwnedLedgerAsync(ledger, cancellationToken);
        }

        ledger.State = MongoMigrationStates.Completed;
        ledger.CompletedAt = DateTime.UtcNow;
        await context.SaveAndReleaseOwnedLedgerAsync(ledger, cancellationToken);
        return context.ToOutcome(ledger, MongoMigrationStates.Completed);
    }

    internal static FilterDefinition<BsonDocument> MarkerFilter(IEnumerable<string> markers) =>
        Builders<BsonDocument>.Filter.Or(markers.Select(marker =>
            Builders<BsonDocument>.Filter.Exists(marker)));
}
