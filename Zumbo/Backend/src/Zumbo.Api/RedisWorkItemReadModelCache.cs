using System.Text;
using System.Text.Json;
using StackExchange.Redis;
using Zumbo.Modules.WorkItems;

public sealed class RedisWorkItemReadModelCache(
    IConnectionMultiplexer connection,
    IConfiguration configuration,
    ILogger<RedisWorkItemReadModelCache> logger) : IWorkItemReadModelCache
{
    private const int MaximumPayloadBytes = 256 * 1024;
    private readonly IDatabase _database = connection.GetDatabase();
    private readonly string _prefix = configuration["ReadModelCache:KeyPrefix"] ?? "zumbo:read-model:";

    public async Task<T> GetOrCreateAsync<T>(
        string projectId,
        string modelName,
        TimeSpan ttl,
        Func<CancellationToken, Task<T>> factory,
        CancellationToken ct)
    {
        ValidateKeyPart(projectId, nameof(projectId));
        ValidateKeyPart(modelName, nameof(modelName));
        var version = await ReadVersionAsync(projectId);
        var cacheKey = DataKey(projectId, version, modelName);

        try
        {
            var cached = await _database.StringGetAsync(cacheKey);
            if (cached.HasValue)
            {
                var value = JsonSerializer.Deserialize<T>((string)cached!, JsonOptions);
                if (value is not null)
                {
                    return value;
                }
            }
        }
        catch (RedisException exception)
        {
            logger.LogWarning(exception, "Read-model cache read failed for {ModelName}.", modelName);
        }

        var created = await factory(ct);
        try
        {
            var payload = JsonSerializer.Serialize(created, JsonOptions);
            if (Encoding.UTF8.GetByteCount(payload) <= MaximumPayloadBytes)
            {
                await _database.StringSetAsync(cacheKey, payload, ttl, When.NotExists);
            }
            else
            {
                logger.LogWarning("Read-model cache payload for {ModelName} exceeded {MaximumBytes} bytes.", modelName, MaximumPayloadBytes);
            }
        }
        catch (RedisException exception)
        {
            logger.LogWarning(exception, "Read-model cache write failed for {ModelName}.", modelName);
        }

        return created;
    }

    public async Task InvalidateProjectAsync(string projectId, CancellationToken ct)
    {
        ValidateKeyPart(projectId, nameof(projectId));
        try
        {
            await _database.StringIncrementAsync(VersionKey(projectId));
        }
        catch (RedisException exception)
        {
            logger.LogWarning(exception, "Read-model cache invalidation failed for project {ProjectId}.", projectId);
        }
    }

    private async Task<long> ReadVersionAsync(string projectId)
    {
        try
        {
            var value = await _database.StringGetAsync(VersionKey(projectId));
            return value.TryParse(out long version) ? version : 0;
        }
        catch (RedisException exception)
        {
            logger.LogWarning(exception, "Read-model cache version read failed for project {ProjectId}.", projectId);
            return 0;
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
}
