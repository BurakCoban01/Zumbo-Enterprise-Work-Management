namespace Zumbo.Modules.WorkItems;

public sealed record WorkItemBulkJobPage(
    IReadOnlyCollection<WorkItemBulkJobResponse> Items,
    int Page,
    int PageSize,
    long TotalCount);
