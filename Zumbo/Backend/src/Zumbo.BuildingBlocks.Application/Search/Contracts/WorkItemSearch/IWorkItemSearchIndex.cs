namespace Zumbo.BuildingBlocks.Application.Search;

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
