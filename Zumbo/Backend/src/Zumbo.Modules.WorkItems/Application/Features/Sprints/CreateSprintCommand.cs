namespace Zumbo.Modules.WorkItems.Application.Features.Sprints;

public sealed record CreateSprintCommand(
    CreateSprintRequest Request,
    string CorrelationId);
