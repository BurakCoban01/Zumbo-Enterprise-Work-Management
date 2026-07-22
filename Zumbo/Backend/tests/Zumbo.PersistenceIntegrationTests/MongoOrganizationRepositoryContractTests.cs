using Microsoft.Extensions.Configuration;
using Zumbo.BuildingBlocks.Infrastructure.Persistence;
using Zumbo.Modules.Organizations;
using Zumbo.RepositoryContracts;

namespace Zumbo.PersistenceIntegrationTests;

public sealed class MongoOrganizationRepositoryContractTests : OrganizationRepositoryContract
{
    protected override Task<OrganizationRepositoryFixture> CreateFixtureAsync()
    {
        var connectionString = Environment.GetEnvironmentVariable("ZUMBO_TEST_MONGO_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "ZUMBO_TEST_MONGO_CONNECTION_STRING is required for organization repository contract tests.");
        }

        var databaseName = "ZumboOrganizationContract" + Guid.NewGuid().ToString("N");
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["MongoDb:ConnectionString"] = connectionString,
                ["MongoDb:DatabaseName"] = databaseName
            })
            .Build();
        var mongo = new MongoDbService(configuration);
        return Task.FromResult<OrganizationRepositoryFixture>(new Fixture(mongo, databaseName));
    }

    private sealed class Fixture(MongoDbService mongo, string databaseName)
        : OrganizationRepositoryFixture(new MongoRepository<OrganizationDocument>(mongo))
    {
        public override async ValueTask DisposeAsync() =>
            await mongo.GetClient("Organizations").DropDatabaseAsync(databaseName);
    }
}
