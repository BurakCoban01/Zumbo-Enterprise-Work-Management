using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed class TeamPerformanceHandler(WorkItemService service)
{
    private TeamPerformancePipeline? pipeline;

    public TeamPerformanceHandler(
        IDocumentRepository<WorkItemDocument> workItems,
        IClock clock,
        ICurrentUser currentUser,
        IProjectPermissionChecker permissionChecker,
        IWorkItemTeamPolicy teamPolicy,
        IWorkItemReadModelCache readModelCache,
        IOptions<WorkItemReadModelCacheOptions> readModelCacheOptions,
        IWorkItemActivityStore activityStore)
        : this(null!)
    {
        pipeline = new TeamPerformancePipeline(
            workItems,
            clock,
            currentUser,
            permissionChecker,
            teamPolicy,
            readModelCache,
            readModelCacheOptions,
            activityStore);
    }

    public Task<WorkItemReportSnapshot<IReadOnlyList<TeamPerformanceResponse>>> HandleAsync(
        TeamPerformanceQuery query,
        CancellationToken ct) =>
        pipeline?.GetAsync(query, ct)
        ?? service.TeamPerformanceSnapshotAsync(query.ProjectId, query.From, query.To, ct);
}
