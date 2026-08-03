using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed partial class CapacityPlanningService{

    private static CapacityPlanResponse ToResponse(CapacityPlanDocument plan, string userId) => new(
        plan.Id,
        plan.OwnerUserId,
        plan.Name,
        plan.Description,
        DateOnlyUtc(plan.PeriodStartUtc),
        DateOnlyUtc(plan.PeriodEndUtc),
        plan.PortfolioId,
        plan.ProjectIds,
        plan.Members.Select(item => new CapacityMemberResponse(
            item.UserId,
            item.TeamId,
            item.WeeklyCapacityHours)).ToList(),
        plan.Allocations.Select(item => new CapacityAllocationResponse(
            item.Id,
            item.UserId,
            item.ProjectId,
            DateOnlyUtc(item.StartDateUtc),
            DateOnlyUtc(item.EndDateUtc),
            item.Percent)).ToList(),
        plan.ViewerUserIds,
        plan.OwnerUserId == userId,
        plan.Archived,
        plan.UpdatedAt,
        plan.Version);
}
