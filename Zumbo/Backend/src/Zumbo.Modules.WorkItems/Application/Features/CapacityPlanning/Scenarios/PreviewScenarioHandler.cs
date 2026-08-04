using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.Modules.WorkItems.Application.Features.CapacityPlanning.Snapshots;
using Zumbo.Modules.WorkItems.Application.Policies.CapacityPlanning;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems.Application.Features.CapacityPlanning.Scenarios;

public sealed class PreviewScenarioHandler(
    IDocumentRepository<CapacityPlanDocument> plans,
    IDocumentRepository<WorkItemDocument> workItems,
    ICapacityPlanningDirectory directory,
    CapacityPlanAccessPolicy access,
    IClock clock)
{
    private readonly SnapshotSourceLoader sourceLoader = new(workItems, directory);
    private readonly SnapshotCalculator calculator = new(clock);

    public async Task<CapacityScenarioResponse> HandleAsync(
        PreviewScenarioQuery query,
        CancellationToken ct)
    {
        var actor = access.CurrentActor();
        var plan = await plans.SelectAsync(
            item => item.Id == query.PlanId
                && item.OrganizationId == actor.OrganizationId
                && !item.Archived,
            ct) ?? throw CapacityPlanAccessPolicy.PlanNotFound();
        access.EnsureOwner(plan, actor.UserId);
        var allocations = CapacityScenarioValidator.Validate(plan, query.Request);
        var source = await sourceLoader.LoadAsync(plan, actor, ct);
        return new CapacityScenarioResponse(
            plan.Id,
            plan.Version,
            calculator.Build(plan, plan.Allocations, source),
            calculator.Build(
                plan,
                allocations.Select(ScenarioAllocationMapper.ToDocument).ToList(),
                source));
    }
}
