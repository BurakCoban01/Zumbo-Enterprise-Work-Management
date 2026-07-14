using System.Collections.Concurrent;

namespace Zumbo.Modules.WorkItems;

public sealed class WorkItemReadModelCacheOptions
{
    public int TtlSeconds { get; init; } = 30;
}

public interface IWorkItemReadModelCache
{
    Task<T> GetOrCreateAsync<T>(
        string projectId,
        string modelName,
        TimeSpan ttl,
        Func<CancellationToken, Task<T>> factory,
        CancellationToken ct);

    Task InvalidateProjectAsync(string projectId, CancellationToken ct);
}

public sealed class InMemoryWorkItemReadModelCache : IWorkItemReadModelCache
{
    private readonly ConcurrentDictionary<string, long> _versions = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, CacheEntry> _entries = new(StringComparer.Ordinal);

    public async Task<T> GetOrCreateAsync<T>(
        string projectId,
        string modelName,
        TimeSpan ttl,
        Func<CancellationToken, Task<T>> factory,
        CancellationToken ct)
    {
        var version = _versions.GetOrAdd(projectId, 0);
        var key = BuildKey(projectId, version, modelName);
        if (_entries.TryGetValue(key, out var entry)
            && entry.ExpiresAt > DateTimeOffset.UtcNow
            && entry.Value is T cached)
        {
            return cached;
        }

        var value = await factory(ct);
        _entries[key] = new CacheEntry(value!, DateTimeOffset.UtcNow.Add(ttl));
        return value;
    }

    public Task InvalidateProjectAsync(string projectId, CancellationToken ct)
    {
        _versions.AddOrUpdate(projectId, 1, static (_, version) => version + 1);
        return Task.CompletedTask;
    }

    private static string BuildKey(string projectId, long version, string modelName) =>
        $"{projectId}:{version}:{modelName}";

    private sealed record CacheEntry(object Value, DateTimeOffset ExpiresAt);
}
