using System.Collections.Concurrent;

namespace Zumbo.Modules.WorkItems;

public sealed record WorkItemReportSnapshot<T>(
    T Data,
    DateTimeOffset GeneratedAt,
    long SourceVersion,
    bool Stale);
