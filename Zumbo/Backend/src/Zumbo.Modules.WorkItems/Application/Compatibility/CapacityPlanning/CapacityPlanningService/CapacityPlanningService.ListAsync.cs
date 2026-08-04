using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed partial class CapacityPlanningService{

    public async Task<CapacityPlanPageResponse> ListAsync(
        bool includeArchived,
        int page,
        int pageSize,
        CancellationToken ct)
    {
        var actor = CurrentActor();
        var normalizedPage = Math.Max(page, 1);
        var normalizedPageSize = Math.Clamp(pageSize, 1, 100);
        var candidates = await plans.ListByFilterAsync(
            item => item.OrganizationId == actor.OrganizationId
                && (includeArchived || !item.Archived)
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
                || await HasVisibleProjectAsync(plan, actor, ct))
            {
                visible.Add(plan);
            }
        }
        return new CapacityPlanPageResponse(
            visible
                .Skip((normalizedPage - 1) * normalizedPageSize)
                .Take(normalizedPageSize)
                .Select(item => ToResponse(item, actor.UserId))
                .ToList(),
            normalizedPage,
            normalizedPageSize,
            visible.Count);
    }
}
