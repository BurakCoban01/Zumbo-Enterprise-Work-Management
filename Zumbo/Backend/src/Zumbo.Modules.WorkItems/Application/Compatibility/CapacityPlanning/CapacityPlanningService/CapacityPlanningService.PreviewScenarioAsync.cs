using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed partial class CapacityPlanningService{

    public async Task<CapacityScenarioResponse> PreviewScenarioAsync(
        string planId,
        CapacityScenarioRequest request,
        CancellationToken ct)
    {
        var actor = CurrentActor();
        var plan = await GetDocumentAsync(planId, includeArchived: false, ct);
        EnsureOwner(plan, actor);
        var allocations = NormalizeAllocations(
            request.Allocations
                ?? throw new ValidationException("Scenario allocations are required."),
            plan.Members.Select(item => item.UserId).ToHashSet(StringComparer.Ordinal),
            plan.ProjectIds.ToHashSet(StringComparer.Ordinal),
            DateOnlyUtc(plan.PeriodStartUtc),
            DateOnlyUtc(plan.PeriodEndUtc));
        var source = await LoadSourceAsync(plan, actor, ct);
        return new CapacityScenarioResponse(
            plan.Id,
            plan.Version,
            BuildSnapshot(plan, plan.Allocations, source),
            BuildSnapshot(plan, allocations.Select(ToDocument).ToList(), source));
    }
}
