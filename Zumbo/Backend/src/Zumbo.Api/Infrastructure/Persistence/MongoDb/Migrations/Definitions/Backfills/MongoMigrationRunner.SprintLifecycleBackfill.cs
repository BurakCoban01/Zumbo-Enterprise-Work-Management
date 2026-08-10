using MongoDB.Bson;
using MongoDB.Driver;

using Zumbo.Api.Infrastructure.Persistence.MongoDb.Migrations.Definitions.Backfills;

public sealed partial class MongoMigrationRunner
{
    private async Task<MongoMigrationOutcome> BackfillSprintLifecycleAsync(CancellationToken cancellationToken) =>
        await new MongoSprintLifecycleBackfill(CreateExecutionContext(), SprintLifecycleMigrationId, SprintLifecycleChecksum).ExecuteAsync(cancellationToken);
    private static FilterDefinition<BsonDocument> SprintLifecycleFilter(BsonValue checkpoint) => MongoSprintLifecycleBackfill.SprintLifecycleFilter(checkpoint);
    private static string LegacySprintId(string projectId, string sprintId) => MongoSprintLifecycleBackfill.LegacySprintId(projectId, sprintId);
}
