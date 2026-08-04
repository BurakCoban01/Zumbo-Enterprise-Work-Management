using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed record CapacityScenarioResponse(
    string PlanId,
    long PlanVersion,
    CapacitySnapshotResponse Baseline,
    CapacitySnapshotResponse Candidate);
