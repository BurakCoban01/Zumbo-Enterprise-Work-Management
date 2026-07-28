using Microsoft.Extensions.Configuration;
using Zumbo.BuildingBlocks.Infrastructure.Persistence;
using Zumbo.Modules.Projects;
using Zumbo.RepositoryContracts;
using ApplicationPersistence = Zumbo.BuildingBlocks.Application.Persistence;

namespace Zumbo.PersistenceIntegrationTests;

public sealed class MongoKnowledgeRepositoryContractTests
    : KnowledgeRepositoryContract, IAsyncLifetime
{
    private readonly MongoDbService mongo;
    private readonly string databaseName;

    public MongoKnowledgeRepositoryContractTests()
    {
        var connection = Environment.GetEnvironmentVariable(
            "ZUMBO_TEST_MONGO_CONNECTION_STRING")
            ?? throw new InvalidOperationException(
                "ZUMBO_TEST_MONGO_CONNECTION_STRING is required.");
        databaseName = "ZumboFeature007Contracts_" + Guid.NewGuid().ToString("N");
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["MongoDb:ConnectionString"] = connection,
                ["MongoDb:DatabaseName"] = databaseName,
                ["Modules:Projects:MongoDb:DatabaseName"] = databaseName
            })
            .Build();
        mongo = new MongoDbService(configuration);
    }

    public Task InitializeAsync() => Task.CompletedTask;
    public Task DisposeAsync() =>
        mongo.GetDatabase("Projects").Client.DropDatabaseAsync(databaseName);

    protected override ApplicationPersistence.IDocumentRepository<KnowledgeDocument>
        Documents() => new MongoRepository<KnowledgeDocument>(mongo);
}
