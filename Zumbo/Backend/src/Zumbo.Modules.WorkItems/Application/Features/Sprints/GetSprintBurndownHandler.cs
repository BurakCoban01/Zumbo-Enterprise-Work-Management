using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems.Application.Features.Sprints;

public sealed class GetSprintBurndownHandler(SprintService service)
{
    private GetSprintBurndownSlice? slice;

    public GetSprintBurndownHandler(
        IDocumentRepository<SprintDocument> sprints,
        IDocumentRepository<SprintScopeSnapshotDocument> scopeSnapshots,
        IDocumentRepository<SprintCompletionSnapshotDocument> completionSnapshots,
        IDocumentRepository<WorkItemDocument> workItems,
        IProjectPermissionChecker permissionChecker,
        ICurrentUser currentUser,
        IOptions<SprintOptions> configuredOptions,
        IWorkItemReadModelCache readModelCache,
        IOptions<WorkItemReadModelCacheOptions> readModelCacheOptions)
        : this(null!)
    {
        slice = new GetSprintBurndownSlice(
            sprints,
            scopeSnapshots,
            completionSnapshots,
            workItems,
            permissionChecker,
            currentUser,
            configuredOptions,
            readModelCache,
            readModelCacheOptions);
    }

    public Task<WorkItemReportSnapshot<IReadOnlyList<SprintBurndownPointResponse>>> HandleAsync(
        GetSprintBurndownQuery query,
        CancellationToken ct) =>
        slice?.HandleAsync(query, ct)
        ?? service.BurndownSnapshotAsync(
            query.ProjectId,
            query.SprintId,
            query.StartDate,
            query.EndDate,
            ct);
}
