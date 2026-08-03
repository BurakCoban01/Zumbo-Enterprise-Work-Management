using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed partial class CapacityPlanningService{

    private static void EnsureOwner(
        CapacityPlanDocument plan,
        (string UserId, string OrganizationId) actor)
    {
        EnsureVisible(plan, actor);
        if (plan.OwnerUserId != actor.UserId)
            throw new ForbiddenException("Only the capacity-plan owner can change this plan.");
    }
}
