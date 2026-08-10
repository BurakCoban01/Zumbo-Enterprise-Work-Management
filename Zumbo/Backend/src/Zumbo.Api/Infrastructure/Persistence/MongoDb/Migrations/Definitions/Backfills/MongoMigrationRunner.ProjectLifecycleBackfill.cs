using MongoDB.Bson;
using MongoDB.Driver;

using Zumbo.Api.Infrastructure.Persistence.MongoDb.Migrations.Definitions.Backfills;

public sealed partial class MongoMigrationRunner
{
    private async Task<MongoMigrationOutcome> BackfillProjectLifecycleAsync(
        CancellationToken cancellationToken) =>
        await new MongoProjectLifecycleBackfill(
            CreateExecutionContext(),
            ProjectLifecycleMigrationId,
            ProjectLifecycleChecksum).ExecuteAsync(cancellationToken);

    private static FilterDefinition<BsonDocument> ProjectLifecycleFilter(BsonValue checkpoint) =>
        MongoProjectLifecycleBackfill.ProjectLifecycleFilter(checkpoint);

    private static FilterDefinition<BsonDocument> ProjectVersionForId(BsonValue id, long version) =>
        MongoProjectLifecycleBackfill.ProjectVersionForId(id, version);

    private static void AddProjectDefault(
        BsonDocument document,
        ICollection<UpdateDefinition<BsonDocument>> updates,
        string field,
        BsonValue value) =>
        MongoProjectLifecycleBackfill.AddProjectDefault(document, updates, field, value);
}
