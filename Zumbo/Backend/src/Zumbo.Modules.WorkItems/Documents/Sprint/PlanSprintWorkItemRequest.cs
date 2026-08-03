using Zumbo.BuildingBlocks.Application.Persistence;

namespace Zumbo.Modules.WorkItems;

public sealed record PlanSprintWorkItemRequest(decimal? EstimatePoints);
