using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using Zumbo.BuildingBlocks.Infrastructure.Persistence;

namespace Zumbo.PersistenceIntegrationTests;

public sealed class MongoDevelopmentIntegrationIndexTests
    : IAsyncLifetime
{
    private readonly string connectionString;
    private readonly MongoClient client;
    private readonly string databaseName;

    public MongoDevelopmentIntegrationIndexTests()
    {
        connectionString = Environment.GetEnvironmentVariable(
            "ZUMBO_TEST_MONGO_CONNECTION_STRING")
            ?? throw new InvalidOperationException(
                "ZUMBO_TEST_MONGO_CONNECTION_STRING is required.");
        client = new MongoClient(connectionString);
        databaseName = "ZumboDevelopmentIndexes_"
            + Guid.NewGuid().ToString("N");
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync() => client.DropDatabaseAsync(databaseName);

    [Fact]
    public async Task IndexesAreTenantScopedUniqueTtlAndIdempotent()
    {
        var catalog = MongoDevelopmentIntegrationIndexes.All;
        Assert.Equal(7, catalog.Count);
        Assert.Equal(
            catalog.Count,
            catalog
                .Select(item => (item.Module, item.Collection, item.Name))
                .Distinct()
                .Count());
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["MongoDb:ConnectionString"] = connectionString,
                ["MongoDb:DatabaseName"] = databaseName,
                ["Modules:WorkItems:MongoDb:DatabaseName"] = databaseName
            })
            .Build();
        var runner = new MongoMigrationRunner(
            new MongoDbService(configuration),
            Options.Create(new MongoMigrationOptions
            {
                BatchSize = 10,
                MaxBatchesPerRun = 20
            }),
            NullLogger<MongoMigrationRunner>.Instance);

        var first = Assert.Single(
            (await runner.RunAsync()).Outcomes,
            item => item.MigrationId
                == MongoMigrationRunner.DevelopmentIntegrationIndexMigrationId);
        var second = Assert.Single(
            (await runner.RunAsync()).Outcomes,
            item => item.MigrationId
                == MongoMigrationRunner.DevelopmentIntegrationIndexMigrationId);
        Assert.Equal(MongoMigrationStates.Completed, first.Status);
        Assert.Equal(catalog.Count, first.Examined);
        Assert.Equal(MongoMigrationStates.Skipped, second.Status);

        var database = client.GetDatabase(databaseName);
        foreach (var specification in catalog)
        {
            var collection = database.GetCollection<BsonDocument>(
                specification.Collection);
            using var cursor = await collection.Indexes.ListAsync();
            var actual = Assert.Single(
                await cursor.ToListAsync(),
                index => index["name"] == specification.Name);
            Assert.Equal(
                specification.Keys,
                actual["key"].AsBsonDocument);
            if (specification.Unique)
            {
                Assert.True(actual["unique"].AsBoolean);
            }
            if (specification.ExpireAfter is not null)
            {
                Assert.Equal(
                    specification.ExpireAfter.Value.TotalSeconds,
                    actual["expireAfterSeconds"].ToDouble());
            }
        }
    }
}
