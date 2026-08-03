using Zumbo.BuildingBlocks.Application.Persistence;

namespace Zumbo.Modules.WorkItems;

public sealed record SprintBacklogItemResponse(
    string Id,
    string Title,
    string Type,
    string Priority,
    decimal EstimatePoints,
    long Rank,
    long Version);
