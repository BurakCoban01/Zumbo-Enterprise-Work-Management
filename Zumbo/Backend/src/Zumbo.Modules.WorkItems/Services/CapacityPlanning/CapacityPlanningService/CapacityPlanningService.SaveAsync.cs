using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed partial class CapacityPlanningService{

    public async Task<CapacityPlanResponse> SaveAsync(
        string? planId,
        SaveCapacityPlanRequest request,
        string correlationId,
        CancellationToken ct)
    {
        var actor = CurrentActor();
        CapacityPlanDocument? existing = null;
        if (planId is not null)
        {
            existing = await GetDocumentAsync(planId, includeArchived: false, ct);
            EnsureOwner(existing, actor);
        }

        var definition = Normalize(request);
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
        if (planId is null)
        {
            var now = clock.UtcNow;
            plan = new CapacityPlanDocument
            {
                OrganizationId = actor.OrganizationId,
                OwnerUserId = actor.UserId,
                CreatedAt = now,
                UpdatedAt = now
            };
            Apply(plan, definition, now);
            plan = await plans.CreateAsync(plan, ct);
            action = "CapacityPlanCreated";
            oldValue = null;
        }
        else
        {
            plan = existing!;
            oldValue = plan.Name;
            Apply(plan, definition, clock.UtcNow);
            await ReplaceAsync(plan, ct);
            action = "CapacityPlanUpdated";
        }

        await audit.WriteAsync(action, plan.Id, oldValue, plan.Name, correlationId, ct);
        return ToResponse(plan, actor.UserId);
    }
}
