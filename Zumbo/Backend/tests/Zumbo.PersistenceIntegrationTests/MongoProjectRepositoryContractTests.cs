using Microsoft.Extensions.Configuration;
using Zumbo.BuildingBlocks.Infrastructure.Persistence;
using Zumbo.Modules.Projects;
using Zumbo.RepositoryContracts;

namespace Zumbo.PersistenceIntegrationTests;

public sealed class MongoProjectRepositoryContractTests : ProjectRepositoryContract
{
    protected override Task<ProjectRepositoryFixture> CreateFixtureAsync()
    {
        var connectionString = Environment.GetEnvironmentVariable("ZUMBO_TEST_MONGO_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "ZUMBO_TEST_MONGO_CONNECTION_STRING is required for project repository contract tests.");
        }

        var databaseName = "ZumboProjectContract" + Guid.NewGuid().ToString("N");
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["MongoDb:ConnectionString"] = connectionString,
                ["MongoDb:DatabaseName"] = databaseName
            })
            .Build();
        var mongo = new MongoDbService(configuration);
        return Task.FromResult<ProjectRepositoryFixture>(new Fixture(mongo, databaseName));
    }

    private sealed class Fixture(MongoDbService mongo, string databaseName)
        : ProjectRepositoryFixture(new MongoRepository<ProjectDocument>(mongo))
    {
        public override async ValueTask DisposeAsync() =>
            await mongo.GetClient("Projects").DropDatabaseAsync(databaseName);
    }
}
