using System.Collections.Concurrent;
using Microsoft.Extensions.Configuration;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Zumbo.BuildingBlocks.Infrastructure.Persistence;

public sealed class MongoDbSettings
{
    public string ConnectionString { get; init; } = "mongodb://localhost:27017";
    public string DatabaseName { get; init; } = "Zumbo";
}

public sealed class PersistenceOptions
{
    public string Provider { get; init; } = "InMemory";
}

public interface IMongoDbService
{
    IMongoCollection<TDocument> GetCollection<TDocument>(string collectionName);
    Task CheckHealthAsync(CancellationToken cancellationToken = default);
}

public sealed class MongoDbService : IMongoDbService
{
    private readonly IConfiguration _configuration;
    private readonly ConcurrentDictionary<string, MongoClient> _clients = new(StringComparer.Ordinal);

    public MongoDbService(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public IMongoCollection<TDocument> GetCollection<TDocument>(string collectionName) =>
        GetDatabase(typeof(TDocument)).GetCollection<TDocument>(collectionName);

    public async Task CheckHealthAsync(CancellationToken cancellationToken = default)
    {
        var modules = _configuration.GetSection("Modules").GetChildren()
            .Select(x => x.Key)
            .Append("Default")
            .Distinct(StringComparer.OrdinalIgnoreCase);
        var targets = modules
            .Select(GetSettings)
            .DistinctBy(x => (x.ConnectionString, x.DatabaseName));
        foreach (var target in targets)
        {
            await GetDatabase(target).RunCommandAsync<BsonDocument>(
                new BsonDocument("ping", 1),
                cancellationToken: cancellationToken);
        }
    }

    private IMongoDatabase GetDatabase(Type documentType)
    {
        return GetDatabase(GetSettings(ResolveModuleName(documentType)));
    }

    private MongoDbSettings GetSettings(string moduleName)
    {
        var section = _configuration.GetSection($"Modules:{moduleName}:MongoDb");
        var global = _configuration.GetSection("MongoDb").Get<MongoDbSettings>() ?? new MongoDbSettings();
        return new MongoDbSettings
        {
            ConnectionString = section["ConnectionString"] ?? global.ConnectionString,
            DatabaseName = section["DatabaseName"] ?? global.DatabaseName
        };
    }

    private IMongoDatabase GetDatabase(MongoDbSettings settings)
    {
        var client = _clients.GetOrAdd(settings.ConnectionString, static connectionString => new MongoClient(connectionString));
        return client.GetDatabase(settings.DatabaseName);
    }

    private static string ResolveModuleName(Type documentType)
    {
        var name = documentType.Namespace ?? string.Empty;
        if (name.Contains("Identity", StringComparison.Ordinal)) return "Identity";
        if (name.Contains("Organizations", StringComparison.Ordinal)) return "Organizations";
        if (name.Contains("Teams", StringComparison.Ordinal)) return "Teams";
        if (name.Contains("Projects", StringComparison.Ordinal)) return "Projects";
        if (name.Contains("Boards", StringComparison.Ordinal)) return "Boards";
        if (name.Contains("WorkItems", StringComparison.Ordinal)) return "WorkItems";
        if (name.Contains("Workflows", StringComparison.Ordinal)) return "Workflows";
        if (name.Contains("Notifications", StringComparison.Ordinal)) return "Notifications";
        if (name.Contains("Audit", StringComparison.Ordinal)) return "Audit";
        return "Default";
    }
}
