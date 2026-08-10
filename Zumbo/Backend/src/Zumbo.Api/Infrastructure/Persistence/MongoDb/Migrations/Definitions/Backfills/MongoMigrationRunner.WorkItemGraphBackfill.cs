using MongoDB.Bson;
using MongoDB.Driver;

using Zumbo.Api.Infrastructure.Persistence.MongoDb.Migrations.Definitions.Backfills;

public sealed partial class MongoMigrationRunner
{
    private async Task<MongoMigrationOutcome> BackfillWorkItemGraphAsync(CancellationToken cancellationToken) =>
        await new MongoWorkItemGraphBackfill(CreateExecutionContext(), WorkItemGraphMigrationId, WorkItemGraphChecksum).ExecuteAsync(cancellationToken);
    private static FilterDefinition<BsonDocument> WorkItemGraphFilter(BsonValue checkpoint) => MongoWorkItemGraphBackfill.WorkItemGraphFilter(checkpoint);
    private static string WorkItemRelationEdgeId(string projectId, string sourceWorkItemId, string targetWorkItemId, string relationType) => MongoWorkItemGraphBackfill.WorkItemRelationEdgeId(projectId, sourceWorkItemId, targetWorkItemId, relationType);
    private static string? NormalizeGraphRelationType(string? relationType) => MongoWorkItemGraphBackfill.NormalizeGraphRelationType(relationType);
}
