namespace Zumbo.Modules.WorkItems.Application.Features.CapacityPlanning;

public sealed record ListCapacityPlansQuery(
    bool IncludeArchived,
    int Page,
    int PageSize);
