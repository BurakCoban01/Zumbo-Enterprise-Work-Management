using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed record CapacityMemberSnapshotResponse(
    string UserId,
    string? TeamId,
    decimal WeeklyCapacityHours,
    decimal CapacityHours,
    decimal AllocatedHours,
    decimal RemainingHours,
    int AllocationPercent,
    string State,
    decimal EstimatedPoints,
    int UnestimatedItems,
    int UnscheduledItems,
    int OpenItems,
    IReadOnlyCollection<CapacityWeekResponse> Weeks,
    IReadOnlyCollection<CapacityTaskResponse> Tasks);
