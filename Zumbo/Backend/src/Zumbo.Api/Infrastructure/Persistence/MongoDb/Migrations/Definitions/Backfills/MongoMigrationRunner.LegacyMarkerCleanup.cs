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
