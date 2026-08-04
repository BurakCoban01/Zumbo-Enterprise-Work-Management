using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.Modules.WorkItems.Application.Policies.CapacityPlanning;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems.Application.Features.CapacityPlanning;

public sealed class SaveCapacityPlanHandler(
    IDocumentRepository<CapacityPlanDocument> plans,
    ICapacityPlanningDirectory directory,
    ICapacityPlanningAuditWriter audit,
    CapacityPlanAccessPolicy access,
    IClock clock,
    IExpectedVersionAccessor? expectedVersions = null)
{
    private readonly ExpectedVersionState expectedVersion = new(expectedVersions);

    public async Task<CapacityPlanResponse> HandleAsync(
        SaveCapacityPlanCommand command,
        CancellationToken ct)
    {
        var actor = access.CurrentActor();
        CapacityPlanDocument? existing = null;
        if (command.PlanId is not null)
        {
            existing = await plans.SelectAsync(
                item => item.Id == command.PlanId
                    && item.OrganizationId == actor.OrganizationId
                    && !item.Archived,
                ct) ?? throw CapacityPlanAccessPolicy.PlanNotFound();
            access.EnsureOwner(existing, actor.UserId);
        }

        var definition = SaveCapacityPlanValidator.Validate(command.Request);
        await directory.EnsureOrganizationUsersAndTeamsAsync(
            actor.OrganizationId,
            definition.Members,
            definition.ViewerUserIds,
            ct);
        await directory.EnsureManageableScopeAsync(
            actor.OrganizationId,
            actor.UserId,
            definition.PortfolioId,
            definition.ProjectIds,
            ct);

        CapacityPlanDocument plan;
        string action;
        string? oldValue;
        if (command.PlanId is null)
        {
            var now = clock.UtcNow;
            plan = new CapacityPlanDocument
            {
                OrganizationId = actor.OrganizationId,
                OwnerUserId = actor.UserId,
                CreatedAt = now,
                UpdatedAt = now
            };
            CapacityPlanDocumentMapper.Apply(plan, definition, now);
            plan = await plans.CreateAsync(plan, ct);
            action = "CapacityPlanCreated";
            oldValue = null;
        }
        else
        {
            plan = existing!;
            oldValue = plan.Name;
            CapacityPlanDocumentMapper.Apply(plan, definition, clock.UtcNow);
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
            action = "CapacityPlanUpdated";
        }

        await audit.WriteAsync(
            action,
            plan.Id,
            oldValue,
            plan.Name,
            command.CorrelationId,
            ct);
        return CapacityPlanResponseMapper.ToResponse(plan, actor.UserId);
    }
}
