using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.Modules.WorkItems.Application.Policies.CapacityPlanning;

namespace Zumbo.Modules.WorkItems.Application.Features.CapacityPlanning;

public sealed class GetCapacityPlanHandler(
    IDocumentRepository<CapacityPlanDocument> plans,
    CapacityPlanAccessPolicy access)
{
    public async Task<CapacityPlanResponse> HandleAsync(
        GetCapacityPlanQuery query,
        CancellationToken ct)
    {
        var actor = access.CurrentActor();
        var plan = await plans.SelectAsync(
            item => item.Id == query.PlanId
                && item.OrganizationId == actor.OrganizationId
                && (query.IncludeArchived || !item.Archived),
            ct) ?? throw CapacityPlanAccessPolicy.PlanNotFound();
        access.EnsureVisible(plan, actor.UserId);
        if (plan.OwnerUserId != actor.UserId
            && !await access.HasVisibleProjectAsync(plan, actor, ct))
        {
            throw CapacityPlanAccessPolicy.PlanNotFound();
        }

        return CapacityPlanResponseMapper.ToResponse(plan, actor.UserId);
    }
}
