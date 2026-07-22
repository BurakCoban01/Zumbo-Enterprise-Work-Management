using Microsoft.Extensions.Configuration;
using Zumbo.BuildingBlocks.Infrastructure.Persistence;
using Zumbo.Modules.Notifications;
using Zumbo.RepositoryContracts;

namespace Zumbo.PersistenceIntegrationTests;

public sealed class MongoNotificationDeliveryRepositoryContractTests
    : NotificationDeliveryRepositoryContract, IAsyncLifetime
{
    private readonly MongoDbService mongo;
    private readonly string databaseName;

    public MongoNotificationDeliveryRepositoryContractTests()
    {
        var connectionString = Environment.GetEnvironmentVariable("ZUMBO_TEST_MONGO_CONNECTION_STRING")
            ?? throw new InvalidOperationException("ZUMBO_TEST_MONGO_CONNECTION_STRING is required.");
        databaseName = "ZumboPlatform003Notification_" + Guid.NewGuid().ToString("N");
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["MongoDb:ConnectionString"] = connectionString,
            ["MongoDb:DatabaseName"] = databaseName,
            ["Modules:Notifications:MongoDb:DatabaseName"] = databaseName
        }).Build();
        mongo = new MongoDbService(configuration);
    }

    public Task InitializeAsync() => Task.CompletedTask;
    public Task DisposeAsync() => mongo.GetDatabase("Notifications").Client.DropDatabaseAsync(databaseName);
    protected override Zumbo.BuildingBlocks.Application.Persistence.IDocumentRepository<NotificationDocument> Repository() =>
        new MongoRepository<NotificationDocument>(mongo);
}
