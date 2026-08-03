using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Search;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed class SearchWorkItemsHandler(WorkItemService service)
{
    private SearchWorkItemsSlice? slice;

    public SearchWorkItemsHandler(
        IDocumentRepository<WorkItemDocument> workItems,
        ICurrentUser currentUser,
        IProjectPermissionChecker permissionChecker,
        IWorkItemTypeSchemaPolicy typeSchemas,
        IWorkItemSearchIndex searchIndex,
        IWorkItemActivityStore activityStore,
        IOptions<SearchOptions> searchOptions)
        : this(null!)
    {
        slice = new SearchWorkItemsSlice(
            workItems,
            currentUser,
            permissionChecker,
            typeSchemas,
            searchIndex,
            activityStore,
            searchOptions);
    }

    public Task<IReadOnlyList<WorkItemResponse>> HandleAsync(WorkItemSearchRequest request, CancellationToken ct) =>
        slice?.HandleAsync(request, ct)
        ?? service.SearchAsync(request, ct);
}
