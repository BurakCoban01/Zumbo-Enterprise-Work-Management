namespace Zumbo.BuildingBlocks.Application.Search;

public sealed record WorkItemSearchRebuildResult(
    string ActiveIndex,
    int Indexed,
    int Removed,
    bool AliasChanged);
