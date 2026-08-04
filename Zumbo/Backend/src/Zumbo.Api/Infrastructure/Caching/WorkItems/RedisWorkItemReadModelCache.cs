using System.Text;
using System.Text.Json;
using StackExchange.Redis;
using Zumbo.BuildingBlocks.Application.Runtime;
using Zumbo.Modules.WorkItems;

public sealed class RedisWorkItemReadModelCache : IWorkItemReadModelCache
{
    private const int MaximumPayloadBytes = 256 * 1024;
    private readonly IDatabase _database;
    private readonly string _prefix;
    private readonly ILogger<RedisWorkItemReadModelCache> logger;
    private readonly IExternalDependencyPolicy? resiliencePolicy;

    public RedisWorkItemReadModelCache(
        IConnectionMultiplexer connection,
        IConfiguration configuration,
        ILogger<RedisWorkItemReadModelCache> logger)
        : this(connection, configuration, logger, null)
    {
    }

    public RedisWorkItemReadModelCache(
        IConnectionMultiplexer connection,
        IConfiguration configuration,
        ILogger<RedisWorkItemReadModelCache> logger,
        IExternalDependencyPolicyProvider? policyProvider)
    {
        _database = connection.GetDatabase();
        _prefix = configuration["ReadModelCache:KeyPrefix"] ?? "zumbo:read-model:";
        this.logger = logger;
        resiliencePolicy = policyProvider?.Get(ExternalDependencyNames.Redis);
    }

    public async Task<T> GetOrCreateAsync<T>(
        string projectId,
        string modelName,
        TimeSpan ttl,
        Func<CancellationToken, Task<T>> factory,
        CancellationToken ct) =>
        (await GetOrCreateSnapshotAsync(projectId, modelName, ttl, factory, ct)).Data;

    public async Task<WorkItemReportSnapshot<T>> GetOrCreateSnapshotAsync<T>(
        string projectId,
        string modelName,
        TimeSpan ttl,
        Func<CancellationToken, Task<T>> factory,
        CancellationToken ct)
    {
        ValidateKeyPart(projectId, nameof(projectId));
        ValidateKeyPart(modelName, nameof(modelName));
        for (var attempt = 0; attempt < 2; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            var (version, versionAvailable) = await ReadVersionAsync(projectId, ct);
            var cacheKey = DataKey(projectId, version, modelName);

            if (versionAvailable)
            {
                try
                {
                    var cached = await ExecuteAsync(
                        "cache-read",
                        ExternalDependencyOperationKind.Read,
                        _ => _database.StringGetAsync(cacheKey),
                        ct);
                    if (cached.HasValue)
                    {
                        var value = JsonSerializer.Deserialize<WorkItemReportSnapshot<T>>(
                            (string)cached!,
                            JsonOptions);
                        if (value is not null)
                        {
                            return value;
                        }
                    }
                }
                catch (Exception exception) when (IsDependencyFailure(exception))
                {
                    logger.LogWarning(exception, "Read-model cache read failed for {ModelName}.", modelName);
                }
            }

            var created = await factory(ct);
            var generatedAt = DateTimeOffset.UtcNow;
            var (currentVersion, currentVersionAvailable) = await ReadVersionAsync(projectId, ct);
            var versionChanged = versionAvailable && currentVersionAvailable && currentVersion != version;
            if (versionChanged && attempt == 0)
            {
                continue;
            }

            var snapshot = new WorkItemReportSnapshot<T>(
                created,
                generatedAt,
                version,
                !versionAvailable || !currentVersionAvailable || versionChanged);
            if (!snapshot.Stale)
            {
                try
                {
                    var payload = JsonSerializer.Serialize(snapshot, JsonOptions);
                    if (Encoding.UTF8.GetByteCount(payload) <= MaximumPayloadBytes)
                    {
                        _ = await ExecuteAsync(
                            "cache-write",
                            ExternalDependencyOperationKind.IdempotentWrite,
                            _ => _database.StringSetAsync(cacheKey, payload, ttl, When.NotExists),
                            ct);
                    }
                    else
                    {
                        logger.LogWarning("Read-model cache payload for {ModelName} exceeded {MaximumBytes} bytes.", modelName, MaximumPayloadBytes);
                    }
                }
                catch (Exception exception) when (IsDependencyFailure(exception))
                {
                    logger.LogWarning(exception, "Read-model cache write failed for {ModelName}.", modelName);
                }
            }

            return snapshot;
        }

        throw new InvalidOperationException("Read-model snapshot generation did not complete.");
    }

    public async Task InvalidateProjectAsync(string projectId, CancellationToken ct)
    {
        ValidateKeyPart(projectId, nameof(projectId));
        try
        {
            _ = await ExecuteAsync(
                "cache-invalidate",
                ExternalDependencyOperationKind.IdempotentWrite,
                _ => _database.StringIncrementAsync(VersionKey(projectId)),
                ct);
        }
        catch (Exception exception) when (IsDependencyFailure(exception))
        {
            logger.LogWarning(exception, "Read-model cache invalidation failed for project {ProjectId}.", projectId);
        }
    }

    private async Task<(long Version, bool Available)> ReadVersionAsync(
        string projectId,
        CancellationToken ct)
    {
        try
        {
            var value = await ExecuteAsync(
                "cache-version-read",
                ExternalDependencyOperationKind.Read,
                _ => _database.StringGetAsync(VersionKey(projectId)),
                ct);
            return (value.TryParse(out long version) ? version : 0, true);
        }
        catch (Exception exception) when (IsDependencyFailure(exception))
        {
            logger.LogWarning(exception, "Read-model cache version read failed for project {ProjectId}.", projectId);
            return (0, false);
        }
    }

    private string VersionKey(string projectId) => $"{_prefix}project:{projectId}:version";
    private string DataKey(string projectId, long version, string modelName) =>
        $"{_prefix}project:{projectId}:v{version}:{modelName}";

    private static void ValidateKeyPart(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 128 || value.Any(char.IsControl))
        {
            throw new ArgumentException("Cache key parts must be non-empty, bounded text.", name);
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private Task<T> ExecuteAsync<T>(
        string operation,
        ExternalDependencyOperationKind kind,
        Func<CancellationToken, Task<T>> action,
        CancellationToken ct) =>
        resiliencePolicy is null
            ? action(ct)
            : resiliencePolicy.ExecuteAsync(operation, kind, action, IsTransient, ct);

    private static bool IsTransient(Exception exception) => exception is RedisException;

    private static bool IsDependencyFailure(Exception exception) =>
        exception is RedisException
            or ExternalDependencyTimeoutException
            or ExternalDependencyCircuitOpenException
            or ExternalDependencyBulkheadRejectedException;
}
