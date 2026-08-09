namespace Zumbo.Modules.WorkItems.Application.Features.Sprints;

public sealed record ListSprintsQuery(string ProjectId, string? After, int PageSize);
