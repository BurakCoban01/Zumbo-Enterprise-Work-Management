using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed class ProjectSummaryHandler(WorkItemService service)
{
    private ProjectSummaryPipeline? pipeline;

    public ProjectSummaryHandler(
        IDocumentRepository<WorkItemDocument> workItems,
        IClock clock,
        ICurrentUser currentUser,
        IProjectPermissionChecker permissionChecker,
        IWorkItemReadModelCache readModelCache,
        IOptions<WorkItemReadModelCacheOptions> readModelCacheOptions)
        : this(null!)
    {
        pipeline = new ProjectSummaryPipeline(
            workItems,
            clock,
            currentUser,
            permissionChecker,
            readModelCache,
            readModelCacheOptions);
    }

    public Task<WorkItemReportSnapshot<ProjectSummaryResponse>> HandleAsync(
        ProjectSummaryQuery query,
        CancellationToken ct) =>
        pipeline?.GetAsync(query, ct)
        ?? service.ProjectSummarySnapshotAsync(query.ProjectId, ct);
}
