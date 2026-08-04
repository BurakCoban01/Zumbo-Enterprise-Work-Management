namespace Zumbo.Modules.WorkItems.Application.Features.CapacityPlanning;

public sealed record SaveCapacityPlanCommand(
    string? PlanId,
    SaveCapacityPlanRequest Request,
    string CorrelationId);
