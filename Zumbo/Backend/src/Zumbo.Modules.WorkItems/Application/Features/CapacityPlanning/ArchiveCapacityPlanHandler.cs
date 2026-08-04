using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.Modules.WorkItems.Application.Policies.CapacityPlanning;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems.Application.Features.CapacityPlanning;

public sealed class ArchiveCapacityPlanHandler(
    IDocumentRepository<CapacityPlanDocument> plans,
    ICapacityPlanningAuditWriter audit,
    CapacityPlanAccessPolicy access,
    IClock clock,
    IExpectedVersionAccessor? expectedVersions = null)
{
    private readonly ExpectedVersionState expectedVersion = new(expectedVersions);

    public async Task HandleAsync(
        ArchiveCapacityPlanCommand command,
        CancellationToken ct)
    {
        var actor = access.CurrentActor();
        var plan = await plans.SelectAsync(
            item => item.Id == command.PlanId
                && item.OrganizationId == actor.OrganizationId
                && !item.Archived,
            ct) ?? throw CapacityPlanAccessPolicy.PlanNotFound();
        access.EnsureOwner(plan, actor.UserId);

        plan.Archived = true;
        plan.UpdatedAt = clock.UtcNow;
        var result = await plans.ReplaceByVersionAsync(
            item => item.Id == plan.Id
                && item.OrganizationId == plan.OrganizationId,
            plan,
            expectedVersion.Consume(plan.Version),
            ct);
        if (!result.Found)
        {
            throw CapacityPlanAccessPolicy.PlanNotFound();
        }

        plan.Version = result.Version!.Value;
        await audit.WriteAsync(
            "CapacityPlanArchived",
            plan.Id,
            plan.Name,
            null,
            command.CorrelationId,
            ct);
    }

}
