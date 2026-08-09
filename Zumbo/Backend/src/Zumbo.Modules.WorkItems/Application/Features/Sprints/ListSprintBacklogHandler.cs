using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems.Application.Features.Sprints;

public sealed class ListSprintBacklogHandler(SprintService service)
{
    private ListSprintBacklogSlice? slice;

    public ListSprintBacklogHandler(
        IDocumentRepository<SprintDocument> sprints,
        IDocumentRepository<WorkItemDocument> workItems,
        IProjectPermissionChecker permissionChecker,
        ICurrentUser currentUser)
        : this(null!) =>
        slice = new ListSprintBacklogSlice(new SprintReadAccess(
            sprints, workItems, permissionChecker, currentUser));

    public Task<SprintBacklogPageResponse> HandleAsync(
        ListSprintBacklogQuery query,
        CancellationToken ct) =>
        slice?.HandleAsync(query, ct)
        ?? service.BacklogAsync(query.ProjectId, query.After, query.PageSize, ct);
}
