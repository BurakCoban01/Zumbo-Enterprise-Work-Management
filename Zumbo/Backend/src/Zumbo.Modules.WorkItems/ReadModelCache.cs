using System.Collections.Concurrent;

namespace Zumbo.Modules.WorkItems;

public sealed class WorkItemReadModelCacheOptions
{
    public int TtlSeconds { get; init; } = 30;
}

public sealed record WorkItemReportSnapshot<T>(
    T Data,
    DateTimeOffset GeneratedAt,
    long SourceVersion,
    bool Stale);

public interface IWorkItemReadModelCache
{
    Task<WorkItemReportSnapshot<T>> GetOrCreateSnapshotAsync<T>(
        string projectId,
        string modelName,
        TimeSpan ttl,
        Func<CancellationToken, Task<T>> factory,
        CancellationToken ct);

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
        CancellationToken ct) =>
        (await GetOrCreateSnapshotAsync(projectId, modelName, ttl, factory, ct)).Data;

    public async Task<WorkItemReportSnapshot<T>> GetOrCreateSnapshotAsync<T>(
        string projectId,
        string modelName,
        TimeSpan ttl,
        Func<CancellationToken, Task<T>> factory,
        CancellationToken ct)
    {
        for (var attempt = 0; attempt < 2; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            var version = _versions.GetOrAdd(projectId, 0);
            var key = BuildKey(projectId, version, modelName);
            if (_entries.TryGetValue(key, out var entry)
                && entry.ExpiresAt > DateTimeOffset.UtcNow
                && entry.Value is WorkItemReportSnapshot<T> cached)
            {
                return cached;
            }

            var value = await factory(ct);
            var generatedAt = DateTimeOffset.UtcNow;
            var currentVersion = _versions.GetOrAdd(projectId, 0);
            if (currentVersion != version && attempt == 0)
            {
                continue;
            }

            var snapshot = new WorkItemReportSnapshot<T>(
                value,
                generatedAt,
                version,
                currentVersion != version);
            if (!snapshot.Stale)
            {
                _entries[key] = new CacheEntry(snapshot, generatedAt.Add(ttl));
            }

            return snapshot;
        }

        throw new InvalidOperationException("Read-model snapshot generation did not complete.");
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
