using Zumbo.BuildingBlocks.Application.Search;

namespace Zumbo.Modules.WorkItems;

public interface IWorkItemCacheInvalidationPublisher
{
    Task InvalidateProjectAsync(string projectId, CancellationToken ct);
}
