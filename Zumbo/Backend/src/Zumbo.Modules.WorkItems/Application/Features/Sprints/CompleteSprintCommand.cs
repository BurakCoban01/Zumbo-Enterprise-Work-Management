namespace Zumbo.Modules.WorkItems.Application.Features.Sprints;

public sealed record CompleteSprintCommand(
    string SprintId,
    CompleteSprintRequest Request,
    string CorrelationId);
