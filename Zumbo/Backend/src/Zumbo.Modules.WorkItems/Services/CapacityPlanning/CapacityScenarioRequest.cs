using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed record CapacityScenarioRequest(
    IReadOnlyCollection<CapacityAllocationRequest> Allocations);
