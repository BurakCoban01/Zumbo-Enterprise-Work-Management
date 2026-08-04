namespace Zumbo.Modules.WorkItems.Application.Features.CapacityPlanning;

public sealed record ShareCapacityPlanCommand(
    string PlanId,
    ShareCapacityPlanRequest Request,
    string CorrelationId);
