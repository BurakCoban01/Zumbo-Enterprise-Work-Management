using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed record CapacityTaskResponse(
    string Id,
    string ProjectId,
    string Title,
    string? AssigneeUserId,
    DateOnly? DueDate,
    decimal? EstimatePoints);
