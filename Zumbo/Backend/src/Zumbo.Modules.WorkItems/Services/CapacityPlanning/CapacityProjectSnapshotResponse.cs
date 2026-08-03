using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed record CapacityProjectSnapshotResponse(
    string ProjectId,
    string Key,
    string Name,
    int AllocatedPeople,
    decimal AllocatedHours,
    int OpenItems,
    decimal EstimatedPoints,
    int UnestimatedItems);
