using System.Collections.Concurrent;

namespace Zumbo.Modules.WorkItems;

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
