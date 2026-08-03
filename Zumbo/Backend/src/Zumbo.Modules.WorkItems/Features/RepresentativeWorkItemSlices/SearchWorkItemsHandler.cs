using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed class SearchWorkItemsHandler(WorkItemService service)
{
    public Task<IReadOnlyList<WorkItemResponse>> HandleAsync(WorkItemSearchRequest request, CancellationToken ct) =>
        service.SearchAsync(request, ct);
}
