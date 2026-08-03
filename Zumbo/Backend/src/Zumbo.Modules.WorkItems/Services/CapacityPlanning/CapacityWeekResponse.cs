using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed record CapacityWeekResponse(
    DateOnly WeekStart,
    decimal CapacityHours,
    decimal AllocatedHours,
    decimal RemainingHours,
    int AllocationPercent,
    string State,
    decimal EstimatedPoints,
    int UnestimatedItems,
    int ScheduledItems);
