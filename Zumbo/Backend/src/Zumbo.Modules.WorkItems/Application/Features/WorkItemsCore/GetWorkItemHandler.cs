using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed class GetWorkItemHandler(WorkItemService service)
{
    private GetWorkItemSlice? slice;

    public GetWorkItemHandler(
        IDocumentRepository<WorkItemDocument> workItems,
        ICurrentUser currentUser,
        IProjectPermissionChecker permissionChecker,
        IWorkItemActivityStore activityStore)
        : this(null!)
    {
        slice = new GetWorkItemSlice(
            workItems,
            currentUser,
            permissionChecker,
            activityStore);
    }

    public Task<WorkItemResponse> HandleAsync(GetWorkItemQuery query, CancellationToken ct) =>
        slice?.HandleAsync(query, ct)
        ?? service.GetAsync(query.Id, ct);
}
