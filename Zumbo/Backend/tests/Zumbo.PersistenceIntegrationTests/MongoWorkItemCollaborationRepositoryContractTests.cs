using Microsoft.Extensions.Configuration;
using Zumbo.BuildingBlocks.Infrastructure.Persistence;
using Zumbo.Modules.WorkItems;
using Zumbo.RepositoryContracts;
using ApplicationPersistence = Zumbo.BuildingBlocks.Application.Persistence;

namespace Zumbo.PersistenceIntegrationTests;

public sealed class MongoWorkItemCollaborationRepositoryContractTests
    : WorkItemCollaborationRepositoryContract, IAsyncLifetime
{
    private readonly MongoDbService mongo;
    private readonly string databaseName;

    public MongoWorkItemCollaborationRepositoryContractTests()
    {
        var connectionString = Environment.GetEnvironmentVariable("ZUMBO_TEST_MONGO_CONNECTION_STRING")
            ?? throw new InvalidOperationException("ZUMBO_TEST_MONGO_CONNECTION_STRING is required.");
        databaseName = "ZumboDomain009Contracts_" + Guid.NewGuid().ToString("N");
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["MongoDb:ConnectionString"] = connectionString,
            ["MongoDb:DatabaseName"] = databaseName,
            ["Modules:WorkItems:MongoDb:DatabaseName"] = databaseName
        }).Build();
        mongo = new MongoDbService(configuration);
    }

    public Task InitializeAsync() => Task.CompletedTask;
    public Task DisposeAsync() => mongo.GetDatabase("WorkItems").Client.DropDatabaseAsync(databaseName);

    protected override ApplicationPersistence.IDocumentRepository<WorkItemCollaborationDocument> Collaborations() => new MongoRepository<WorkItemCollaborationDocument>(mongo);
    protected override ApplicationPersistence.IDocumentRepository<WorkItemEventActivityDocument> Activities() => new MongoRepository<WorkItemEventActivityDocument>(mongo);
    protected override ApplicationPersistence.IDocumentRepository<WorkItemTemplateDocument> Templates() => new MongoRepository<WorkItemTemplateDocument>(mongo);
    protected override ApplicationPersistence.IDocumentRepository<WorkItemRecurrenceDocument> Recurrences() => new MongoRepository<WorkItemRecurrenceDocument>(mongo);
    protected override ApplicationPersistence.IDocumentRepository<WorkItemRecurrenceOccurrenceDocument> Occurrences() => new MongoRepository<WorkItemRecurrenceOccurrenceDocument>(mongo);
}
