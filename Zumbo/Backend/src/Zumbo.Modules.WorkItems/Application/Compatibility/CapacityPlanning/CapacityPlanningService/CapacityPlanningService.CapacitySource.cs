using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed partial class CapacityPlanningService{

    private sealed record CapacitySource(
        IReadOnlyCollection<CapacityProjectAccess> Projects,
        IReadOnlyCollection<string> UnavailableProjectIds,
        IReadOnlyCollection<WorkItemDocument> Tasks,
        bool Truncated);
}
