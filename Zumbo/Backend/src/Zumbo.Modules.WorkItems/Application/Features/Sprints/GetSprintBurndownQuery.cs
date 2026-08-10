namespace Zumbo.Modules.WorkItems.Application.Features.Sprints;

public sealed record GetSprintBurndownQuery(
    string ProjectId,
    string SprintId,
    DateOnly? StartDate,
    DateOnly? EndDate);
