using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.Modules.WorkItems.Application.Policies.CapacityPlanning;

namespace Zumbo.Modules.WorkItems.Application.Features.CapacityPlanning;

public sealed class ListCapacityPlansHandler(
    IDocumentRepository<CapacityPlanDocument> plans,
    CapacityPlanAccessPolicy access)
{
    public async Task<CapacityPlanPageResponse> HandleAsync(
        ListCapacityPlansQuery query,
        CancellationToken ct)
    {
        var actor = access.CurrentActor();
        var normalizedPage = Math.Max(query.Page, 1);
        var normalizedPageSize = Math.Clamp(query.PageSize, 1, 100);
        var candidates = await plans.ListByFilterAsync(
            item => item.OrganizationId == actor.OrganizationId
                && (query.IncludeArchived || !item.Archived)
                && (item.OwnerUserId == actor.UserId
                    || item.ViewerUserIds.Contains(actor.UserId)),
            item => item.UpdatedAt,
            orderDescending: true,
            page: 1,
            pageSize: 500,
            cancellationToken: ct);
        var visible = new List<CapacityPlanDocument>();
        foreach (var plan in candidates)
        {
            if (plan.OwnerUserId == actor.UserId
                || await access.HasVisibleProjectAsync(plan, actor, ct))
            {
                visible.Add(plan);
            }
        }

        return new CapacityPlanPageResponse(
            visible
                .Skip((normalizedPage - 1) * normalizedPageSize)
                .Take(normalizedPageSize)
                .Select(item => CapacityPlanResponseMapper.ToResponse(
                    item,
                    actor.UserId))
                .ToList(),
            normalizedPage,
            normalizedPageSize,
            visible.Count);
    }
}
