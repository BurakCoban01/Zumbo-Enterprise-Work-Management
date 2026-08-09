namespace Zumbo.Modules.WorkItems.Application.Features.Sprints;

public sealed record PlanSprintWorkItemCommand(
    string SprintId,
    string WorkItemId,
    PlanSprintWorkItemRequest Request,
    string CorrelationId);
