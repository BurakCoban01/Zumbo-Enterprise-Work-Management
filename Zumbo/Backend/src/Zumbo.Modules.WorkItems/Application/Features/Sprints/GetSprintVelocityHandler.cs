using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems.Application.Features.Sprints;

public sealed class GetSprintVelocityHandler(SprintService service)
{
    private GetSprintVelocitySlice? slice;

    public GetSprintVelocityHandler(
        IDocumentRepository<SprintDocument> sprints,
        IProjectPermissionChecker permissionChecker,
        ICurrentUser currentUser,
        IWorkItemReadModelCache readModelCache,
        IOptions<WorkItemReadModelCacheOptions> readModelCacheOptions)
        : this(null!)
    {
        slice = new GetSprintVelocitySlice(
            sprints,
            permissionChecker,
            currentUser,
            readModelCache,
            readModelCacheOptions);
    }

    public Task<WorkItemReportSnapshot<IReadOnlyList<SprintVelocityResponse>>> HandleAsync(
        GetSprintVelocityQuery query,
        CancellationToken ct) =>
        slice?.HandleAsync(query, ct)
        ?? service.VelocitySnapshotAsync(query.ProjectId, query.SprintCount, ct);
}
