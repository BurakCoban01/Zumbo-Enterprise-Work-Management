using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed partial class CapacityPlanningService{

    public async Task<CapacityPlanResponse> ShareAsync(
        string planId,
        ShareCapacityPlanRequest request,
        string correlationId,
        CancellationToken ct)
    {
        var actor = CurrentActor();
        var plan = await GetDocumentAsync(planId, includeArchived: false, ct);
        EnsureOwner(plan, actor);
        var viewers = NormalizeIds(
            request.ViewerUserIds
                ?? throw new ValidationException("Capacity-plan viewer list is required."),
            MaximumViewers,
            "Capacity-plan viewer");
        if (viewers.Contains(actor.UserId, StringComparer.Ordinal))
            throw new ValidationException("Capacity-plan owner cannot also be a viewer.");
        await directory.EnsureOrganizationUsersAndTeamsAsync(
            actor.OrganizationId,
            [],
            viewers,
            ct);
        var oldValue = string.Join(",", plan.ViewerUserIds.Order(StringComparer.Ordinal));
        plan.ViewerUserIds = viewers;
        plan.UpdatedAt = clock.UtcNow;
        await ReplaceAsync(plan, ct);
        await audit.WriteAsync(
            "CapacityPlanSharingUpdated",
            plan.Id,
            oldValue,
            string.Join(",", viewers.Order(StringComparer.Ordinal)),
            correlationId,
            ct);
        return ToResponse(plan, actor.UserId);
    }
}
