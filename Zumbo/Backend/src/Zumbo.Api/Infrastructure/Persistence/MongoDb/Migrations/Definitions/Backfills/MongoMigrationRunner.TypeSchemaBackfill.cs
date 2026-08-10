using MongoDB.Bson;
using MongoDB.Driver;

using Zumbo.Api.Infrastructure.Persistence.MongoDb.Migrations.Definitions.Backfills;

public sealed partial class MongoMigrationRunner
{
    private async Task<MongoMigrationOutcome> BackfillWorkItemTypeSchemasAsync(CancellationToken cancellationToken) =>
        await new MongoWorkItemTypeSchemaBackfill(CreateExecutionContext(), WorkItemTypeSchemaMigrationId, WorkItemTypeSchemaChecksum, DefaultIssueTypeKeys).ExecuteAsync(cancellationToken);
    private static FilterDefinition<BsonDocument> WorkItemTypeSchemaFilter(BsonValue checkpoint) => MongoWorkItemTypeSchemaBackfill.WorkItemTypeSchemaFilter(checkpoint);
    private static BsonArray DefaultIssueTypes() => MongoWorkItemTypeSchemaBackfill.DefaultIssueTypes();
    private static BsonArray DefaultIssueTypeLayouts() => MongoWorkItemTypeSchemaBackfill.DefaultIssueTypeLayouts(DefaultIssueTypeKeys);
    private static BsonDocument IssueType(string key, string name, string hierarchy, int position) => MongoWorkItemTypeSchemaBackfill.IssueType(key, name, hierarchy, position);
}
