using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems.Application.Policies.CapacityPlanning;

public sealed class CapacityPlanAccessPolicy(
    ICapacityPlanningDirectory directory,
    ICurrentUser currentUser)
{
    public (string UserId, string OrganizationId) CurrentActor()
    {
        var userId = currentUser.UserId
            ?? throw new UnauthorizedException("Authenticated user is required.");
        var organizationId = currentUser.OrganizationId
            ?? throw new UnauthorizedException("Active organization is required.");
        return (userId, organizationId);
    }

    public void EnsureVisible(CapacityPlanDocument plan, string userId)
    {
        if (plan.OwnerUserId != userId
            && !plan.ViewerUserIds.Contains(userId, StringComparer.Ordinal))
        {
            throw PlanNotFound();
        }
    }

    public void EnsureOwner(CapacityPlanDocument plan, string userId)
    {
        EnsureVisible(plan, userId);
        if (plan.OwnerUserId != userId)
        {
            throw new ForbiddenException(
                "Only the capacity-plan owner can change this plan.");
        }
    }

    public async Task<bool> HasVisibleProjectAsync(
        CapacityPlanDocument plan,
        (string UserId, string OrganizationId) actor,
        CancellationToken ct) =>
        (await directory.ReadProjectAccessAsync(
            actor.OrganizationId,
            actor.UserId,
            plan.ProjectIds,
            ct)).Any(item => item.Available);

    public static NotFoundException PlanNotFound() => new(
        "CAPACITY_PLAN_NOT_FOUND",
        "Capacity plan was not found.");
}
