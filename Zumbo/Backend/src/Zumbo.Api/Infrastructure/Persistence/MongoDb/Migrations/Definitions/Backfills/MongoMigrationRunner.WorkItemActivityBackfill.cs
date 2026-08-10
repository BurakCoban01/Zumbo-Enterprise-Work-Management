using MongoDB.Bson;
using MongoDB.Driver;

using Zumbo.Api.Infrastructure.Persistence.MongoDb.Migrations.Definitions.Backfills;

public sealed partial class MongoMigrationRunner
{
    private async Task<MongoMigrationOutcome> BackfillWorkItemActivitiesAsync(
        CancellationToken cancellationToken)
    {
        return await new MongoWorkItemActivityBackfill(
                CreateExecutionContext(),
                WorkItemActivityMigrationId,
                WorkItemActivityChecksum)
            .ExecuteAsync(cancellationToken);
    }

    private static FilterDefinition<BsonDocument> WorkItemActivityFilter(BsonValue checkpoint) =>
        MongoWorkItemActivityBackfill.WorkItemActivityFilter(checkpoint);

    private static FilterDefinition<BsonDocument> WorkItemActivityVersionFilter() =>
        MongoWorkItemActivityBackfill.WorkItemActivityVersionFilter();

    private static bool HasMigratableActivities(BsonDocument workItem) =>
        MongoWorkItemActivityBackfill.HasMigratableActivities(
            workItem,
            ArrayValue,
            StringValue,
            static value => TryResolveUtc(value, out var utc) ? utc : null);

    private async Task UpsertWorkItemActivitiesAsync(
        BsonDocument workItem,
        string organizationId,
        string projectId,
        string workItemId,
        CancellationToken cancellationToken)
    {
        await new MongoWorkItemActivityBackfill(
                CreateExecutionContext(),
                WorkItemActivityMigrationId,
                WorkItemActivityChecksum)
            .UpsertWorkItemActivitiesAsync(
                workItem,
                organizationId,
                projectId,
                workItemId,
                cancellationToken);
    }

    private static string ActivityId(params string[] parts) =>
        MongoWorkItemActivityBackfill.ActivityId(parts);

    private async Task CopyArrayAsync(
        IMongoCollection<BsonDocument> target,
        BsonDocument workItem,
        string field,
        string organizationId,
        string projectId,
        string workItemId,
        IReadOnlyCollection<string> copiedFields,
        CancellationToken cancellationToken)
    {
        await new MongoWorkItemActivityBackfill(
                CreateExecutionContext(),
                WorkItemActivityMigrationId,
                WorkItemActivityChecksum)
            .CopyArrayAsync(
                target,
                workItem,
                field,
                organizationId,
                projectId,
                workItemId,
                copiedFields,
                cancellationToken);
    }

    private static async Task ReplaceMigratedActivityAsync(
        IMongoCollection<BsonDocument> collection,
        BsonDocument expected,
        CancellationToken cancellationToken)
    {
        await MongoWorkItemActivityBackfill.ReplaceMigratedActivityAsync(
            collection,
            expected,
            cancellationToken);
    }
}
