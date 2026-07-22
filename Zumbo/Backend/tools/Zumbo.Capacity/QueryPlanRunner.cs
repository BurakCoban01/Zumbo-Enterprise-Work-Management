using MongoDB.Bson;
using MongoDB.Driver;

namespace Zumbo.Capacity;

internal sealed class QueryPlanRunner(string mongoConnectionString)
{
    private readonly MongoClient _mongo = new(mongoConnectionString);

    public async Task<IReadOnlyList<QueryPlanResult>> RunAsync(CapacityProfile profile, CancellationToken ct)
    {
        var workItemCommand = Explain(
            "workitems",
            new BsonDocument
            {
                ["ProjectId"] = CapacityIds.Project(profile, 0),
                ["Archived"] = false,
                ["_id"] = new BsonDocument("$gt", CapacityIds.WorkItem(profile, 0))
            },
            new BsonDocument("_id", 1));
        var auditCommand = Explain(
            "auditlogs",
            new BsonDocument
            {
                ["OrganizationId"] = CapacityIds.Organization(profile, 0),
                ["ActorUserId"] = CapacityIds.User(profile, 0)
            },
            new BsonDocument { ["CreatedAt"] = -1, ["_id"] = 1 });

        return
        [
            await ExecuteAsync(
                _mongo.GetDatabase("ZumboWorkItems"),
                "workitems-project-cursor",
                "ix_workitems_project_archived_id",
                workItemCommand,
                200,
                250,
                ct),
            await ExecuteAsync(
                _mongo.GetDatabase("ZumboAudit"),
                "audit-tenant-actor-cursor",
                "ix_auditlogs_organization_actor_created",
                auditCommand,
                200,
                250,
                ct)
        ];
    }

    private static BsonDocument Explain(string collection, BsonDocument filter, BsonDocument sort) => new()
    {
        ["explain"] = new BsonDocument
        {
            ["find"] = collection,
            ["filter"] = filter,
            ["sort"] = sort,
            ["limit"] = 200
        },
        ["verbosity"] = "executionStats"
    };

    private static async Task<QueryPlanResult> ExecuteAsync(
        IMongoDatabase database,
        string name,
        string expectedIndex,
        BsonDocument command,
        long maximumDocumentsExamined,
        long maximumExecutionMilliseconds,
        CancellationToken ct)
    {
        _ = await database.RunCommandAsync<BsonDocument>(command, cancellationToken: ct);
        var explain = await database.RunCommandAsync<BsonDocument>(command, cancellationToken: ct);
        var serialized = explain.ToJson();
        var execution = explain["executionStats"].AsBsonDocument;
        var examined = execution["totalDocsExamined"].ToInt64();
        var returned = execution["nReturned"].ToInt64();
        var elapsed = execution["executionTimeMillis"].ToInt64();
        var indexUsed = serialized.Contains(expectedIndex, StringComparison.Ordinal);
        var collectionScan = serialized.Contains("COLLSCAN", StringComparison.Ordinal);
        return new QueryPlanResult(
            name,
            expectedIndex,
            indexUsed,
            collectionScan,
            examined,
            returned,
            elapsed,
            maximumDocumentsExamined,
            maximumExecutionMilliseconds,
            indexUsed && !collectionScan && examined <= maximumDocumentsExamined && elapsed <= maximumExecutionMilliseconds);
    }
}
