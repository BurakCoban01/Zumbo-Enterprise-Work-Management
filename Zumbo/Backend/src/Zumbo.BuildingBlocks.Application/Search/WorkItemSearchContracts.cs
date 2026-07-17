namespace Zumbo.BuildingBlocks.Application.Search;

public sealed class SearchOptions
{
    public string Provider { get; init; } = "InMemory";
    public int DegradedFallbackMaxItems { get; init; } = 1_000;
}

public sealed class WorkItemSearchUnavailableException : Exception
{
    public WorkItemSearchUnavailableException(string message, Exception? innerException = null)
        : base(message, innerException) { }
}

public sealed record WorkItemSearchRecord(
    string Id,
    string ProjectId,
    string BoardId,
    string Title,
    string Description,
    string Status,
    string Priority,
    string? AssigneeUserId,
    IReadOnlyCollection<string> Labels,
    string Type = "Task",
    string CustomFieldSearchText = "",
    IReadOnlyCollection<string>? CustomFieldExactValues = null,
    string OrganizationId = "");

public sealed record WorkItemSearchQuery(
    string OrganizationId,
    string ProjectId,
    string? Text,
    string? AssigneeUserId,
    string? Status,
    int Page = 1,
    int PageSize = 100,
    string? IssueType = null,
    string? CustomFieldKey = null,
    string? CustomFieldValue = null);

public sealed record WorkItemSearchResult(
    IReadOnlyList<string> Ids,
    long TotalCount,
    bool Degraded = false);

public sealed record WorkItemSearchRebuildResult(
    string ActiveIndex,
    int Indexed,
    int Removed,
    bool AliasChanged);

public interface IWorkItemSearchIndex
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
    Task IndexAsync(WorkItemSearchRecord record, CancellationToken cancellationToken = default);
    Task DeleteAsync(string id, CancellationToken cancellationToken = default);
    Task<WorkItemSearchResult> SearchAsync(
        WorkItemSearchQuery query,
        CancellationToken cancellationToken = default);
    Task<WorkItemSearchRebuildResult> RebuildAsync(
        IReadOnlyCollection<WorkItemSearchRecord> records,
        CancellationToken cancellationToken = default);
}
