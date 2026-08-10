namespace Zumbo.Modules.Projects.Application.Features.Goals;

public sealed record ListGoalsQuery(
    bool IncludeArchived,
    int Page,
    int PageSize);
