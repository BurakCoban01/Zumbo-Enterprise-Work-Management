namespace Zumbo.BuildingBlocks.Application.Search;

public sealed record WorkItemSearchResult(
    IReadOnlyList<string> Ids,
    long TotalCount,
    bool Degraded = false);
