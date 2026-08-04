namespace Zumbo.Modules.WorkItems;

public sealed record IntakeSubmissionPage(
    IReadOnlyCollection<IntakeSubmissionResponse> Items,
    int Page,
    int PageSize,
    long TotalCount);
