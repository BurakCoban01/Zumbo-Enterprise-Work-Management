using Microsoft.Extensions.Configuration;
using Zumbo.BuildingBlocks.Infrastructure.Persistence;
using Zumbo.Modules.WorkItems;
using Zumbo.RepositoryContracts;
using ApplicationPersistence = Zumbo.BuildingBlocks.Application.Persistence;

namespace Zumbo.PersistenceIntegrationTests;

public sealed class MongoCapacityPlanRepositoryContractTests
    : CapacityPlanRepositoryContract, IAsyncLifetime
{
    private readonly MongoDbService mongo;
    private readonly string databaseName;

    public MongoCapacityPlanRepositoryContractTests()
    {
        var connection = Environment.GetEnvironmentVariable(
            "ZUMBO_TEST_MONGO_CONNECTION_STRING")
            ?? throw new InvalidOperationException(
                "ZUMBO_TEST_MONGO_CONNECTION_STRING is required.");
        databaseName = "ZumboFeature006Contracts_" + Guid.NewGuid().ToString("N");
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["MongoDb:ConnectionString"] = connection,
                ["MongoDb:DatabaseName"] = databaseName,
                ["Modules:WorkItems:MongoDb:DatabaseName"] = databaseName
            })
            .Build();
        mongo = new MongoDbService(configuration);
    }

    public Task InitializeAsync() => Task.CompletedTask;
    public Task DisposeAsync() =>
        mongo.GetDatabase("WorkItems").Client.DropDatabaseAsync(databaseName);

    protected override ApplicationPersistence.IDocumentRepository<CapacityPlanDocument> Plans() =>
        new MongoRepository<CapacityPlanDocument>(mongo);
}
