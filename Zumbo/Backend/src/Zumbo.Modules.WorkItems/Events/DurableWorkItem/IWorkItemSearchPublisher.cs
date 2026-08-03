using Zumbo.BuildingBlocks.Application.Search;

namespace Zumbo.Modules.WorkItems;

public interface IWorkItemSearchPublisher
{
    Task IndexAsync(WorkItemSearchRecord record, CancellationToken ct);
    Task DeleteAsync(string workItemId, CancellationToken ct);
}
