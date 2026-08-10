namespace Zumbo.Modules.WorkItems.Application.Features.Recurrences;

public sealed record ListWorkItemTemplatesQuery(
    string ProjectId,
    int Page,
    int PageSize,
    bool IncludeArchived);
