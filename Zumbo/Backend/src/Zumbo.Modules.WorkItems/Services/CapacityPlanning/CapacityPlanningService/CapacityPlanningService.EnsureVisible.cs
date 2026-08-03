using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed partial class CapacityPlanningService{

    private static void EnsureVisible(
        CapacityPlanDocument plan,
        (string UserId, string OrganizationId) actor)
    {
        if (plan.OwnerUserId != actor.UserId
            && !plan.ViewerUserIds.Contains(actor.UserId, StringComparer.Ordinal))
        {
            throw new NotFoundException(
                "CAPACITY_PLAN_NOT_FOUND",
                "Capacity plan was not found.");
        }
    }
}
