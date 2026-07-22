using Microsoft.Extensions.Configuration;
using MongoDB.Driver;
using Zumbo.BuildingBlocks.Infrastructure.Persistence;

namespace Zumbo.UnitTests;

public sealed class MongoDbResilienceConfigurationTests
{
    [Fact]
    public void DriverReceivesExplicitTimeoutPoolAndRetrySettings()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["MongoDb:ConnectionString"] = "mongodb://127.0.0.1:27017",
            ["MongoDb:DatabaseName"] = "zumbo_test",
            ["MongoDb:ConnectTimeoutSeconds"] = "7",
            ["MongoDb:ServerSelectionTimeoutSeconds"] = "8",
            ["MongoDb:SocketTimeoutSeconds"] = "9",
            ["MongoDb:WaitQueueTimeoutSeconds"] = "6",
            ["MongoDb:MinimumPoolSize"] = "2",
            ["MongoDb:MaximumPoolSize"] = "23",
            ["MongoDb:RetryReads"] = "true",
            ["MongoDb:RetryWrites"] = "false"
        }).Build();

        var client = Assert.IsType<MongoClient>(new MongoDbService(configuration).GetClient("Default"));

        Assert.Equal(TimeSpan.FromSeconds(7), client.Settings.ConnectTimeout);
        Assert.Equal(TimeSpan.FromSeconds(8), client.Settings.ServerSelectionTimeout);
        Assert.Equal(TimeSpan.FromSeconds(9), client.Settings.SocketTimeout);
        Assert.Equal(TimeSpan.FromSeconds(6), client.Settings.WaitQueueTimeout);
        Assert.Equal(2, client.Settings.MinConnectionPoolSize);
        Assert.Equal(23, client.Settings.MaxConnectionPoolSize);
        Assert.True(client.Settings.RetryReads);
        Assert.False(client.Settings.RetryWrites);
    }

    [Fact]
    public void ModuleSettingsOverrideGlobalBoundsWithoutDroppingDefaults()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["MongoDb:ConnectionString"] = "mongodb://127.0.0.1:27017",
            ["MongoDb:DatabaseName"] = "global",
            ["MongoDb:MaximumPoolSize"] = "50",
            ["Modules:WorkItems:MongoDb:DatabaseName"] = "work_items",
            ["Modules:WorkItems:MongoDb:MaximumPoolSize"] = "12"
        }).Build();

        var database = new MongoDbService(configuration).GetDatabase("WorkItems");
        var client = Assert.IsType<MongoClient>(database.Client);

        Assert.Equal("work_items", database.DatabaseNamespace.DatabaseName);
        Assert.Equal(12, client.Settings.MaxConnectionPoolSize);
        Assert.Equal(TimeSpan.FromSeconds(5), client.Settings.ConnectTimeout);
    }
}
