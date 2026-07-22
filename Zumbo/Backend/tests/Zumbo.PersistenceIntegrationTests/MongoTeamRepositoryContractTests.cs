using Microsoft.Extensions.Configuration;
using Zumbo.BuildingBlocks.Infrastructure.Persistence;
using Zumbo.Modules.Teams;
using Zumbo.RepositoryContracts;

namespace Zumbo.PersistenceIntegrationTests;

public sealed class MongoTeamRepositoryContractTests : TeamRepositoryContract
{
    protected override Task<TeamRepositoryFixture> CreateFixtureAsync()
    {
        var connectionString = Environment.GetEnvironmentVariable("ZUMBO_TEST_MONGO_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "ZUMBO_TEST_MONGO_CONNECTION_STRING is required for team repository contract tests.");
        }

        var databaseName = "ZumboTeamContract" + Guid.NewGuid().ToString("N");
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["MongoDb:ConnectionString"] = connectionString,
                ["MongoDb:DatabaseName"] = databaseName
            })
            .Build();
        var mongo = new MongoDbService(configuration);
        return Task.FromResult<TeamRepositoryFixture>(new Fixture(mongo, databaseName));
    }

    private sealed class Fixture(MongoDbService mongo, string databaseName)
        : TeamRepositoryFixture(new MongoRepository<TeamDocument>(mongo))
    {
        public override async ValueTask DisposeAsync() =>
            await mongo.GetClient("Teams").DropDatabaseAsync(databaseName);
    }
}
