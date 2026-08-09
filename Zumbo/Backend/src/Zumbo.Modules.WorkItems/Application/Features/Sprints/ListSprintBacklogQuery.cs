namespace Zumbo.Modules.WorkItems.Application.Features.Sprints;

public sealed record ListSprintBacklogQuery(string ProjectId, string? After, int PageSize);
