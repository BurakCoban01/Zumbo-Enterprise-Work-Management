using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed partial class CapacityPlanningService{

    private async Task ReplaceAsync(CapacityPlanDocument plan, CancellationToken ct)
    {
        var result = await plans.ReplaceByVersionAsync(
            item => item.Id == plan.Id && item.OrganizationId == plan.OrganizationId,
            plan,
            expectedVersion.Consume(plan.Version),
            ct);
        if (!result.Found)
            throw new NotFoundException("CAPACITY_PLAN_NOT_FOUND", "Capacity plan was not found.");
        plan.Version = result.Version!.Value;
    }
}
