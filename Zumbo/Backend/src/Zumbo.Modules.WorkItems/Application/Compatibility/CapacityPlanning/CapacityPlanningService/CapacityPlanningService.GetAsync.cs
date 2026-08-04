using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed partial class CapacityPlanningService{

    public async Task<CapacityPlanResponse> GetAsync(
        string planId,
        bool includeArchived,
        CancellationToken ct)
    {
        var actor = CurrentActor();
        var plan = await GetDocumentAsync(planId, includeArchived, ct);
        EnsureVisible(plan, actor);
        if (plan.OwnerUserId != actor.UserId
            && !await HasVisibleProjectAsync(plan, actor, ct))
        {
            throw new NotFoundException(
                "CAPACITY_PLAN_NOT_FOUND",
                "Capacity plan was not found.");
        }
        return ToResponse(plan, actor.UserId);
    }
}
