using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using Zumbo.BuildingBlocks.Infrastructure.Persistence;
using Zumbo.Modules.Identity;
using Zumbo.Modules.Projects;

namespace Zumbo.PersistenceIntegrationTests;

public sealed class MongoHighCardinalityQueryPlanTests : IAsyncLifetime
{
    private readonly string connectionString;
    private readonly string databaseName = $"ZumboHighCardinality_{Guid.NewGuid():N}";
    private readonly MongoDbService mongo;

    public MongoHighCardinalityQueryPlanTests()
    {
        connectionString = Environment.GetEnvironmentVariable("ZUMBO_TEST_MONGO_CONNECTION_STRING")
            ?? throw new InvalidOperationException(
                "ZUMBO_TEST_MONGO_CONNECTION_STRING is required for high-cardinality query-plan tests.");
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["MongoDb:ConnectionString"] = connectionString,
                ["MongoDb:DatabaseName"] = databaseName,
                ["Modules:Identity:MongoDb:DatabaseName"] = databaseName,
                ["Modules:Projects:MongoDb:DatabaseName"] = databaseName
            })
            .Build();
        mongo = new MongoDbService(configuration);
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync() =>
        await mongo.GetClient("Default").DropDatabaseAsync(databaseName);

    [Fact]
    public async Task ProjectAndSessionQueries_UseBoundedAccessIndexes()
    {
        var runner = new MongoMigrationRunner(
            mongo,
            Options.Create(new MongoMigrationOptions()),
            NullLogger<MongoMigrationRunner>.Instance);
        var report = await runner.RunAsync(CancellationToken.None);
        Assert.Contains(
            report.Outcomes,
            outcome => outcome.MigrationId == MongoMigrationRunner.HighCardinalityIndexMigrationId
                && outcome.Status == MongoMigrationStates.Completed);

        var projects = mongo.GetCollection<BsonDocument>("projects", "Projects");
        var sessions = mongo.GetCollection<BsonDocument>("refreshsessions", "Identity");
        await projects.InsertManyAsync(Enumerable.Range(1, 5_000).Select(Project).ToList());
        await sessions.InsertManyAsync(Enumerable.Range(1, 5_000).Select(Session).ToList());

        var projectExplain = await ExplainAsync(
            "projects",
            new BsonDocument
            {
                ["OrganizationId"] = "target-org",
                ["Archived"] = false
            },
            new BsonDocument { ["Key"] = 1, ["_id"] = 1 },
            "Projects");
        AssertPlan(
            projectExplain,
            "ix_projects_organization_archived_key",
            maximumDocumentsExamined: 200);

        var sessionExplain = await ExplainAsync(
            "refreshsessions",
            new BsonDocument
            {
                ["OrganizationId"] = "target-org",
                ["UserId"] = "target-user"
            },
            new BsonDocument { ["LastSeenAt"] = -1, ["_id"] = 1 },
            "Identity");
        AssertPlan(
            sessionExplain,
            "ix_refreshsessions_owner_last_seen",
            maximumDocumentsExamined: 200);
        Assert.DoesNotContain("\"stage\" : \"SORT\"", sessionExplain.ToJson(), StringComparison.Ordinal);
    }

    private async Task<BsonDocument> ExplainAsync(
        string collection,
        BsonDocument filter,
        BsonDocument sort,
        string module) =>
        await mongo.GetDatabase(module).RunCommandAsync<BsonDocument>(new BsonDocument
        {
            ["explain"] = new BsonDocument
            {
                ["find"] = collection,
                ["filter"] = filter,
                ["sort"] = sort,
                ["limit"] = 100
            },
            ["verbosity"] = "executionStats"
        });

    private static void AssertPlan(
        BsonDocument explain,
        string indexName,
        long maximumDocumentsExamined)
    {
        var plan = explain.ToJson();
        var execution = explain["executionStats"].AsBsonDocument;
        Assert.Contains(indexName, plan, StringComparison.Ordinal);
        Assert.DoesNotContain("COLLSCAN", plan, StringComparison.Ordinal);
        Assert.InRange(
            execution["totalDocsExamined"].ToInt64(),
            1,
            maximumDocumentsExamined);
        Assert.InRange(execution["executionTimeMillis"].ToInt64(), 0, 250);
    }

    private static BsonDocument Project(int index) =>
        new ProjectDocument
        {
            Id = $"plan-project-{index:D5}",
            OrganizationId = index <= 1_000 ? "target-org" : "other-org",
            Key = $"P{index:D7}",
            Name = $"Project {index:D5}",
            Archived = false,
            Members =
            [
                new ProjectMemberDocument
                {
                    UserId = "owner",
                    Role = ProjectRoles.Owner
                }
            ],
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            Version = 1
        }.ToBsonDocument();

    private static BsonDocument Session(int index)
    {
        var at = new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero).AddMinutes(index);
        return new RefreshSessionDocument
        {
            Id = $"plan-session-{index:D5}",
            OrganizationId = index <= 1_000 ? "target-org" : "other-org",
            UserId = index <= 1_000 ? "target-user" : "other-user",
            TokenHash = $"token-{index:D5}",
            CreatedAt = at,
            LastSeenAt = at,
            ExpiresAt = at.AddDays(14),
            ExpiresAtUtc = at.AddDays(14).UtcDateTime,
            RetainUntilUtc = at.AddDays(44).UtcDateTime,
            Version = 1
        }.ToBsonDocument();
    }
}
