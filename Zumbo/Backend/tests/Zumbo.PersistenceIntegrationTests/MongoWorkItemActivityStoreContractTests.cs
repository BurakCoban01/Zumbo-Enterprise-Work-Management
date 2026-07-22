using Microsoft.Extensions.Configuration;
using Zumbo.BuildingBlocks.Infrastructure.Persistence;
using Zumbo.Modules.WorkItems;
using Zumbo.RepositoryContracts;
using ApplicationPersistence = Zumbo.BuildingBlocks.Application.Persistence;

namespace Zumbo.PersistenceIntegrationTests;

public sealed class MongoWorkItemActivityStoreContractTests : WorkItemActivityStoreContract, IAsyncLifetime
{
    private readonly MongoDbService mongo;
    private readonly string databaseName;

    public MongoWorkItemActivityStoreContractTests()
    {
        var connectionString = Environment.GetEnvironmentVariable("ZUMBO_TEST_MONGO_CONNECTION_STRING")
            ?? throw new InvalidOperationException("ZUMBO_TEST_MONGO_CONNECTION_STRING is required.");
        databaseName = $"ZumboData007Contracts_{Guid.NewGuid():N}";
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

    protected override ApplicationPersistence.IDocumentRepository<WorkItemCommentActivityDocument> Comments() => new MongoRepository<WorkItemCommentActivityDocument>(mongo);
    protected override ApplicationPersistence.IDocumentRepository<WorkItemCommentRevisionActivityDocument> Revisions() => new MongoRepository<WorkItemCommentRevisionActivityDocument>(mongo);
    protected override ApplicationPersistence.IDocumentRepository<WorkItemAttachmentActivityDocument> Attachments() => new MongoRepository<WorkItemAttachmentActivityDocument>(mongo);
    protected override ApplicationPersistence.IDocumentRepository<WorkItemWorkLogActivityDocument> WorkLogs() => new MongoRepository<WorkItemWorkLogActivityDocument>(mongo);
    protected override ApplicationPersistence.IDocumentRepository<WorkItemApprovalActivityDocument> Approvals() => new MongoRepository<WorkItemApprovalActivityDocument>(mongo);
    protected override ApplicationPersistence.IDocumentRepository<WorkItemTimelineActivityDocument> Timeline() => new MongoRepository<WorkItemTimelineActivityDocument>(mongo);
}
