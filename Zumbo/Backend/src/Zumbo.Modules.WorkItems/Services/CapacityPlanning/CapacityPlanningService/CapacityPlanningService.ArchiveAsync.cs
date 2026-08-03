using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed partial class CapacityPlanningService{

    public async Task ArchiveAsync(
        string planId,
        string correlationId,
        CancellationToken ct)
    {
        var actor = CurrentActor();
        var plan = await GetDocumentAsync(planId, includeArchived: false, ct);
        EnsureOwner(plan, actor);
        plan.Archived = true;
        plan.UpdatedAt = clock.UtcNow;
        await ReplaceAsync(plan, ct);
        await audit.WriteAsync(
            "CapacityPlanArchived",
            plan.Id,
            plan.Name,
            null,
            correlationId,
            ct);
    }
}
