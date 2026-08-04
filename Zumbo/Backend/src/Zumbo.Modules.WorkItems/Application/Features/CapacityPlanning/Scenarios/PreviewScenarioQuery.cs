namespace Zumbo.Modules.WorkItems.Application.Features.CapacityPlanning.Scenarios;

public sealed record PreviewScenarioQuery(
    string PlanId,
    CapacityScenarioRequest Request);
