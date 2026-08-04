using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed partial class CapacityPlanningService{

    public async Task<CapacitySnapshotResponse> GetSnapshotAsync(
        string planId,
        CancellationToken ct)
    {
        var actor = CurrentActor();
        var plan = await GetDocumentAsync(planId, includeArchived: false, ct);
        EnsureVisible(plan, actor);
        var source = await LoadSourceAsync(plan, actor, ct);
        return BuildSnapshot(plan, plan.Allocations, source);
    }
}
