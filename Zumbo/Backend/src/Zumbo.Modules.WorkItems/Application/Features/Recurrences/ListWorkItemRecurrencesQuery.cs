namespace Zumbo.Modules.WorkItems.Application.Features.Recurrences;

public sealed record ListWorkItemRecurrencesQuery(
    string ProjectId,
    int Page,
    int PageSize,
    bool IncludeArchived);
