namespace Zumbo.Modules.WorkItems.Application.Features.Sprints;

public sealed record StartSprintCommand(
    string SprintId,
    string CorrelationId);
