using Microsoft.Extensions.Configuration;
using Zumbo.BuildingBlocks.Infrastructure.Persistence;
using Zumbo.Modules.Boards;
using Zumbo.Modules.WorkItems;
using Zumbo.Modules.Workflows;
using Zumbo.RepositoryContracts;

namespace Zumbo.PersistenceIntegrationTests;

public sealed class MongoWorkflowBoardRepositoryContractTests : WorkflowBoardRepositoryContract
{
    protected override Task<WorkflowBoardRepositoryFixture> CreateFixtureAsync()
    {
        var connectionString = Environment.GetEnvironmentVariable("ZUMBO_TEST_MONGO_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("ZUMBO_TEST_MONGO_CONNECTION_STRING is required for workflow/board contract tests.");
        }

        var databaseName = "ZumboWorkflowBoardContract" + Guid.NewGuid().ToString("N");
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["MongoDb:ConnectionString"] = connectionString,
                ["MongoDb:DatabaseName"] = databaseName
            })
            .Build();
        var mongo = new MongoDbService(configuration);
        return Task.FromResult<WorkflowBoardRepositoryFixture>(new Fixture(mongo, databaseName));
    }

    private sealed class Fixture(MongoDbService mongo, string databaseName)
        : WorkflowBoardRepositoryFixture(
            new MongoRepository<WorkflowDefinitionDocument>(mongo),
            new MongoRepository<BoardDocument>(mongo),
            new MongoRepository<WorkItemDocument>(mongo),
            new MongoRepository<BoardColumnWipProjectionDocument>(mongo),
            new MongoRepository<SprintDocument>(mongo),
            new MongoRepository<SprintScopeSnapshotDocument>(mongo),
            new MongoRepository<SprintCompletionSnapshotDocument>(mongo))
    {
        public override async ValueTask DisposeAsync() =>
            await mongo.GetClient("WorkItems").DropDatabaseAsync(databaseName);
    }
}
