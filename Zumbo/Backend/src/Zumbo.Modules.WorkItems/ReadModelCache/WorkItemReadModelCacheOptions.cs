using System.Collections.Concurrent;

namespace Zumbo.Modules.WorkItems;

public sealed class WorkItemReadModelCacheOptions
{
    public int TtlSeconds { get; init; } = 30;
}
