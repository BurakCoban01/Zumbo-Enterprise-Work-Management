using System.Collections.Concurrent;
using Microsoft.Extensions.Configuration;
using MongoDB.Bson;
using MongoDB.Driver;
using Zumbo.BuildingBlocks.Application.Runtime;

namespace Zumbo.BuildingBlocks.Infrastructure.Persistence;

public sealed class MongoDbService : IMongoDbService
{
    private readonly IConfiguration _configuration;
    private readonly ConcurrentDictionary<string, MongoClient> _clients = new(StringComparer.Ordinal);
    private readonly IExternalDependencyPolicy? _resiliencePolicy;

    public MongoDbService(IConfiguration configuration)
        : this(configuration, null)
    {
    }

    public MongoDbService(
        IConfiguration configuration,
        IExternalDependencyPolicyProvider? policyProvider)
    {
        _configuration = configuration;
        _resiliencePolicy = policyProvider?.Get(ExternalDependencyNames.MongoDb);
    }

    public IMongoCollection<TDocument> GetCollection<TDocument>(string collectionName) =>
        GetDatabase(typeof(TDocument)).GetCollection<TDocument>(collectionName);

    public IMongoCollection<TDocument> GetCollection<TDocument>(string collectionName, string moduleName)
    {
        if (string.IsNullOrWhiteSpace(moduleName))
        {
            throw new InvalidOperationException("MongoDB module name must be explicit and non-empty.");
        }

        return GetDatabase(GetSettings(moduleName.Trim())).GetCollection<TDocument>(collectionName);
    }

    public IMongoDatabase GetDatabase(string moduleName)
    {
        if (string.IsNullOrWhiteSpace(moduleName))
        {
            throw new InvalidOperationException("MongoDB module name must be explicit and non-empty.");
        }

        return GetDatabase(GetSettings(moduleName.Trim()));
    }

    public IMongoClient GetClient(string moduleName) => GetDatabase(moduleName).Client;

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
            if (_resiliencePolicy is null)
            {
                await PingAsync(target, cancellationToken);
            }
            else
            {
                await _resiliencePolicy.ExecuteAsync(
                    "health",
                    ExternalDependencyOperationKind.Health,
                    token => PingAsync(target, token),
                    IsTransient,
                    cancellationToken);
            }
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
            DatabaseName = section["DatabaseName"] ?? global.DatabaseName,
            ConnectTimeoutSeconds = section.GetValue<int?>("ConnectTimeoutSeconds") ?? global.ConnectTimeoutSeconds,
            ServerSelectionTimeoutSeconds = section.GetValue<int?>("ServerSelectionTimeoutSeconds") ?? global.ServerSelectionTimeoutSeconds,
            SocketTimeoutSeconds = section.GetValue<int?>("SocketTimeoutSeconds") ?? global.SocketTimeoutSeconds,
            WaitQueueTimeoutSeconds = section.GetValue<int?>("WaitQueueTimeoutSeconds") ?? global.WaitQueueTimeoutSeconds,
            MinimumPoolSize = section.GetValue<int?>("MinimumPoolSize") ?? global.MinimumPoolSize,
            MaximumPoolSize = section.GetValue<int?>("MaximumPoolSize") ?? global.MaximumPoolSize,
            RetryReads = section.GetValue<bool?>("RetryReads") ?? global.RetryReads,
            RetryWrites = section.GetValue<bool?>("RetryWrites") ?? global.RetryWrites
        };
    }

    private IMongoDatabase GetDatabase(MongoDbSettings settings)
    {
        settings.Validate();
        var key = string.Join('\u001f',
            settings.ConnectionString,
            settings.ConnectTimeoutSeconds,
            settings.ServerSelectionTimeoutSeconds,
            settings.SocketTimeoutSeconds,
            settings.WaitQueueTimeoutSeconds,
            settings.MinimumPoolSize,
            settings.MaximumPoolSize,
            settings.RetryReads,
            settings.RetryWrites);
        var client = _clients.GetOrAdd(key, _ => CreateClient(settings));
        return client.GetDatabase(settings.DatabaseName);
    }

    private static MongoClient CreateClient(MongoDbSettings configured)
    {
        var settings = MongoClientSettings.FromConnectionString(configured.ConnectionString);
        settings.ConnectTimeout = TimeSpan.FromSeconds(configured.ConnectTimeoutSeconds);
        settings.ServerSelectionTimeout = TimeSpan.FromSeconds(configured.ServerSelectionTimeoutSeconds);
        settings.SocketTimeout = TimeSpan.FromSeconds(configured.SocketTimeoutSeconds);
        settings.WaitQueueTimeout = TimeSpan.FromSeconds(configured.WaitQueueTimeoutSeconds);
        settings.MinConnectionPoolSize = configured.MinimumPoolSize;
        settings.MaxConnectionPoolSize = configured.MaximumPoolSize;
        settings.RetryReads = configured.RetryReads;
        settings.RetryWrites = configured.RetryWrites;
        return new MongoClient(settings);
    }

    private async Task PingAsync(MongoDbSettings settings, CancellationToken cancellationToken) =>
        _ = await GetDatabase(settings).RunCommandAsync<BsonDocument>(
            new BsonDocument("ping", 1),
            cancellationToken: cancellationToken);

    private static bool IsTransient(Exception exception) =>
        exception is MongoConnectionException
            or MongoExecutionTimeoutException
            or TimeoutException;

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
