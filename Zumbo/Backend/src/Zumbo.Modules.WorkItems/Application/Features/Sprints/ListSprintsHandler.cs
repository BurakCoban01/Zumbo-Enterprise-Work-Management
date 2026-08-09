using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems.Application.Features.Sprints;

public sealed class ListSprintsHandler(SprintService service)
{
    private ListSprintsSlice? slice;

    public ListSprintsHandler(
        IDocumentRepository<SprintDocument> sprints,
        IDocumentRepository<WorkItemDocument> workItems,
        IProjectPermissionChecker permissionChecker,
        ICurrentUser currentUser)
        : this(null!) =>
        slice = new ListSprintsSlice(new SprintReadAccess(
            sprints, workItems, permissionChecker, currentUser));

    public Task<SprintCursorPageResponse> HandleAsync(ListSprintsQuery query, CancellationToken ct) =>
        slice?.HandleAsync(query, ct)
        ?? service.ListAsync(query.ProjectId, query.After, query.PageSize, ct);
}
