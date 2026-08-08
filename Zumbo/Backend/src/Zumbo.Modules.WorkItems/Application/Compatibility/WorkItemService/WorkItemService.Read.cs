using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Linq.Expressions;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Runtime;
using Zumbo.BuildingBlocks.Application.Search;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed partial class WorkItemService
{
    public async Task<IReadOnlyList<WorkItemResponse>> SearchAsync(WorkItemSearchRequest request, CancellationToken ct) =>
        await searchWorkItemsHandler.HandleAsync(request, ct);

    public async Task<WorkItemSearchPageResponse> SearchPageAsync(WorkItemSearchRequest request, CancellationToken ct)
        => await searchWorkItemsHandler.HandlePageAsync(request, ct);

    public async Task<WorkItemResponse> GetAsync(string id, CancellationToken ct)
        => await getWorkItemHandler.HandleAsync(new GetWorkItemQuery(id), ct);
}
