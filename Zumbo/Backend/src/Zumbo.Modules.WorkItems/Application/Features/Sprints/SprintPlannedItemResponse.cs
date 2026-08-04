using Zumbo.BuildingBlocks.Application.Persistence;

namespace Zumbo.Modules.WorkItems;

public sealed record SprintPlannedItemResponse(
    string WorkItemId,
    string? SprintId,
    decimal EstimatePoints,
    long Version) : IVersionedResource;
