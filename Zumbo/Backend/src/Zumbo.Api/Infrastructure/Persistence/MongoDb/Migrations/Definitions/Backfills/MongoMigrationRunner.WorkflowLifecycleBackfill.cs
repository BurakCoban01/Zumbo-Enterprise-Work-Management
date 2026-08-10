using MongoDB.Bson;
using MongoDB.Driver;

using Zumbo.Api.Infrastructure.Persistence.MongoDb.Migrations.Definitions.Backfills;

public sealed partial class MongoMigrationRunner
{
    private async Task<MongoMigrationOutcome> BackfillWorkflowLifecycleAsync(CancellationToken cancellationToken) =>
        await new MongoWorkflowLifecycleBackfill(CreateExecutionContext(), WorkflowLifecycleMigrationId, WorkflowLifecycleChecksum).ExecuteAsync(cancellationToken);
    private static FilterDefinition<BsonDocument> WorkflowLifecycleFilter(BsonValue checkpoint) => MongoWorkflowLifecycleBackfill.WorkflowLifecycleFilter(checkpoint);
    private static FilterDefinition<BsonDocument> WorkflowVersionForId(BsonValue id, long version) => MongoWorkflowLifecycleBackfill.WorkflowVersionForId(id, version);
}
