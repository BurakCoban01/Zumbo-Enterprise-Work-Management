using Zumbo.BuildingBlocks.Application.Persistence;

namespace Zumbo.Modules.WorkItems;

public sealed record CreateSprintRequest(
    string ProjectId,
    string Name,
    string? Goal,
    DateOnly StartDate,
    DateOnly EndDate);
