using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.Modules.WorkItems.Application.Policies.CapacityPlanning;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems.Application.Features.CapacityPlanning.Snapshots;

public sealed class GetCapacitySnapshotHandler(
    IDocumentRepository<CapacityPlanDocument> plans,
    IDocumentRepository<WorkItemDocument> workItems,
    ICapacityPlanningDirectory directory,
    CapacityPlanAccessPolicy access,
    IClock clock)
{
    private readonly SnapshotSourceLoader sourceLoader = new(workItems, directory);
    private readonly SnapshotCalculator calculator = new(clock);

    public async Task<CapacitySnapshotResponse> HandleAsync(
        GetCapacitySnapshotQuery query,
        CancellationToken ct)
    {
        var actor = access.CurrentActor();
        var plan = await plans.SelectAsync(
            item => item.Id == query.PlanId
                && item.OrganizationId == actor.OrganizationId
                && !item.Archived,
            ct) ?? throw CapacityPlanAccessPolicy.PlanNotFound();
        access.EnsureVisible(plan, actor.UserId);
        var source = await sourceLoader.LoadAsync(plan, actor, ct);
        return calculator.Build(plan, plan.Allocations, source);
    }
}
