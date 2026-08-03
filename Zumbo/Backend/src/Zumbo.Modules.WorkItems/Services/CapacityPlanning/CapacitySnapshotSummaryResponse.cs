using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed record CapacitySnapshotSummaryResponse(
    int People,
    decimal CapacityHours,
    decimal AllocatedHours,
    decimal RemainingHours,
    int OverCapacityPeople,
    int OpenItems,
    decimal EstimatedPoints,
    int UnestimatedItems,
    int UnscheduledItems);
