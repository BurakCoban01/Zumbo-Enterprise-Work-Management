namespace Zumbo.Modules.WorkItems.Application.Features.Sprints;

internal static class SprintResponseMapper
{
    internal static SprintResponse ToResponse(SprintDocument sprint) =>
        new(
            sprint.Id,
            sprint.ProjectId,
            sprint.Name,
            sprint.Goal,
            DateOnly.FromDateTime(sprint.StartAtUtc.UtcDateTime),
            DateOnly.FromDateTime(sprint.EndAtUtc.UtcDateTime),
            sprint.Status,
            sprint.CommittedItems,
            sprint.CommittedPoints,
            sprint.CompletedItems,
            sprint.CompletedPoints,
            sprint.CarryoverItems,
            sprint.CarryoverPoints,
            sprint.StartedAt,
            sprint.CompletedAt,
            sprint.Version);
}
