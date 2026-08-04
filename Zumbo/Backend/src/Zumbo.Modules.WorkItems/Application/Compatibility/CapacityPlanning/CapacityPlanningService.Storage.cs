using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.Modules.WorkItems.Application.Features.CapacityPlanning;
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

    private static void Apply(
        CapacityPlanDocument plan,
        SaveCapacityPlanRequest request,
        DateTimeOffset now)
    {
        plan.Name = request.Name;
        plan.Description = request.Description;
        plan.PeriodStartUtc = UtcDay(request.PeriodStart);
        plan.PeriodEndUtc = UtcDay(request.PeriodEnd);
        plan.PortfolioId = request.PortfolioId;
        plan.ProjectIds = request.ProjectIds.ToList();
        plan.Members = request.Members.Select(item => new CapacityMemberDocument
        {
            UserId = item.UserId,
            TeamId = item.TeamId,
            WeeklyCapacityHours = item.WeeklyCapacityHours
        }).ToList();
        plan.Allocations = request.Allocations.Select(ToDocument).ToList();
        plan.ViewerUserIds = request.ViewerUserIds.ToList();
        plan.UpdatedAt = now;
    }

    private static CapacityAllocationDocument ToDocument(CapacityAllocationRequest item) => new()
    {
        Id = item.Id!,
        UserId = item.UserId,
        ProjectId = item.ProjectId,
        StartDateUtc = UtcDay(item.StartDate),
        EndDateUtc = UtcDay(item.EndDate),
        Percent = item.Percent
    };

    private static CapacityPlanResponse ToResponse(
        CapacityPlanDocument plan,
        string userId) =>
        CapacityPlanResponseMapper.ToResponse(plan, userId);

    private static DateTimeOffset UtcDay(DateOnly value) =>
        new(value.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
}
