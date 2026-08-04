namespace Zumbo.Modules.WorkItems.Application.Features.CapacityPlanning;

public sealed record ArchiveCapacityPlanCommand(
    string PlanId,
    string CorrelationId);
