namespace Zumbo.Modules.WorkItems.Application.Features.CapacityPlanning;

public sealed record GetCapacityPlanQuery(
    string PlanId,
    bool IncludeArchived);
