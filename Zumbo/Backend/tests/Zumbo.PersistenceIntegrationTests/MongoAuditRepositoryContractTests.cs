using Microsoft.Extensions.Configuration;
using Zumbo.BuildingBlocks.Infrastructure.Persistence;
using Zumbo.Modules.Audit;
using Zumbo.RepositoryContracts;

namespace Zumbo.PersistenceIntegrationTests;

public sealed class MongoAuditRepositoryContractTests : AuditRepositoryContract, IAsyncLifetime
{
    private readonly MongoDbService mongo;
    private readonly string databaseName;

    public MongoAuditRepositoryContractTests()
    {
        var connectionString = Environment.GetEnvironmentVariable("ZUMBO_TEST_MONGO_CONNECTION_STRING")
            ?? throw new InvalidOperationException("ZUMBO_TEST_MONGO_CONNECTION_STRING is required.");
        databaseName = "ZumboPlatform002Audit_" + Guid.NewGuid().ToString("N");
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["MongoDb:ConnectionString"] = connectionString,
            ["MongoDb:DatabaseName"] = databaseName,
            ["Modules:Audit:MongoDb:DatabaseName"] = databaseName
        }).Build();
        mongo = new MongoDbService(configuration);
    }

    public Task InitializeAsync() => Task.CompletedTask;
    public Task DisposeAsync() => mongo.GetDatabase("Audit").Client.DropDatabaseAsync(databaseName);
    protected override Zumbo.BuildingBlocks.Application.Persistence.IDocumentRepository<AuditLogDocument> Repository() =>
        new MongoRepository<AuditLogDocument>(mongo);
}
