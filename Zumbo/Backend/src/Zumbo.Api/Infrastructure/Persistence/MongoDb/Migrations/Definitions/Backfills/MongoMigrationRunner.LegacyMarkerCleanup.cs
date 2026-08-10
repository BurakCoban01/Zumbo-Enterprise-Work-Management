using Zumbo.Api.Infrastructure.Persistence.MongoDb.Migrations.Definitions.Backfills;

public sealed partial class MongoMigrationRunner
{
    private Task<MongoMigrationOutcome> CleanupLegacyMigrationMarkersAsync(
        CancellationToken cancellationToken) =>
        new MongoLegacyMarkerCleanup(
            CreateExecutionContext(),
            LegacyMigrationMarkerCleanupId,
            LegacyMigrationMarkerCleanupChecksum)
            .ExecuteAsync(cancellationToken);
}
