using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed record CapacityTeamSnapshotResponse(
    string TeamId,
    int Members,
    decimal CapacityHours,
    decimal AllocatedHours,
    decimal RemainingHours,
    string State,
    int OpenItems,
    int UnestimatedItems);
