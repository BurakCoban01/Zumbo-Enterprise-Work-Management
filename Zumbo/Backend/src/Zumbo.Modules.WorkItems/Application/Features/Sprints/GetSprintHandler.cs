using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems.Application.Features.Sprints;

public sealed class GetSprintHandler(SprintService service)
{
    private GetSprintSlice? slice;

    public GetSprintHandler(
        IDocumentRepository<SprintDocument> sprints,
        IDocumentRepository<WorkItemDocument> workItems,
        IProjectPermissionChecker permissionChecker,
        ICurrentUser currentUser)
        : this(null!) =>
        slice = new GetSprintSlice(new SprintReadAccess(
            sprints, workItems, permissionChecker, currentUser));

    public Task<SprintResponse> HandleAsync(GetSprintQuery query, CancellationToken ct) =>
        slice?.HandleAsync(query, ct) ?? service.GetAsync(query.SprintId, ct);
}
