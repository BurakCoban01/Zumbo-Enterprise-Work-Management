using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed partial class CapacityPlanningService{

    private async Task<CapacityPlanDocument> GetDocumentAsync(
        string planId,
        bool includeArchived,
        CancellationToken ct)
    {
        var actor = CurrentActor();
        return await plans.SelectAsync(
            item => item.Id == planId
                && item.OrganizationId == actor.OrganizationId
                && (includeArchived || !item.Archived),
            ct)
            ?? throw new NotFoundException(
                "CAPACITY_PLAN_NOT_FOUND",
                "Capacity plan was not found.");
    }
}
