using Microsoft.Extensions.Configuration;
using Zumbo.BuildingBlocks.Infrastructure.Persistence;
using Zumbo.Modules.Identity;
using Zumbo.RepositoryContracts;
using ApplicationPersistence = Zumbo.BuildingBlocks.Application.Persistence;

namespace Zumbo.PersistenceIntegrationTests;

public sealed class MongoIdentityCredentialStoreContractTests : IdentityCredentialStoreContract, IAsyncLifetime
{
    private readonly ApplicationPersistence.IDocumentRepository<RefreshSessionDocument> sessions;
    private readonly ApplicationPersistence.IDocumentRepository<ApiKeyDocument> apiKeys;
    private readonly MongoDbService mongo;
    private readonly string databaseName;

    public MongoIdentityCredentialStoreContractTests()
    {
        var connectionString = Environment.GetEnvironmentVariable("ZUMBO_TEST_MONGO_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "ZUMBO_TEST_MONGO_CONNECTION_STRING is required for real Mongo credential contract tests.");
        }

        databaseName = $"ZumboData006Contracts_{Guid.NewGuid():N}";
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["MongoDb:ConnectionString"] = connectionString,
                ["MongoDb:DatabaseName"] = databaseName,
                ["Modules:Identity:MongoDb:DatabaseName"] = databaseName
            })
            .Build();
        mongo = new MongoDbService(configuration);
        sessions = new MongoRepository<RefreshSessionDocument>(mongo);
        apiKeys = new MongoRepository<ApiKeyDocument>(mongo);
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync() => mongo.GetDatabase("Identity").Client.DropDatabaseAsync(databaseName);

    protected override ApplicationPersistence.IDocumentRepository<RefreshSessionDocument>
        CreateSessionRepository() => sessions;

    protected override ApplicationPersistence.IDocumentRepository<ApiKeyDocument>
        CreateApiKeyRepository() => apiKeys;
}
