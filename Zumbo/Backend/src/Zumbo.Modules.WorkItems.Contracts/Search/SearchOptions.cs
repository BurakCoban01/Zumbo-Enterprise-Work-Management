namespace Zumbo.BuildingBlocks.Application.Search;

public sealed class SearchOptions
{
    public string Provider { get; init; } = "InMemory";
    public int DegradedFallbackMaxItems { get; init; } = 1_000;
}
