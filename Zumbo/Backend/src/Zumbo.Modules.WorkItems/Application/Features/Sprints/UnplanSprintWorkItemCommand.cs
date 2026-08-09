namespace Zumbo.Modules.WorkItems.Application.Features.Sprints;

public sealed record UnplanSprintWorkItemCommand(
    string SprintId,
    string WorkItemId,
    string CorrelationId);
