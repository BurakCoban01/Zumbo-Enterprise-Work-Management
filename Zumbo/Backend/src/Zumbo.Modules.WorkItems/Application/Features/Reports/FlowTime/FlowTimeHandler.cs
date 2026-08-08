using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed class FlowTimeHandler(WorkItemService service)
{
    private FlowTimePipeline? pipeline;

    public FlowTimeHandler(
        IDocumentRepository<WorkItemDocument> workItems,
        IClock clock,
        ICurrentUser currentUser,
        IProjectPermissionChecker permissionChecker,
        IWorkItemReadModelCache readModelCache,
        IOptions<WorkItemReadModelCacheOptions> readModelCacheOptions,
        IWorkItemActivityStore activityStore)
        : this(null!)
    {
        pipeline = new FlowTimePipeline(
            workItems,
            clock,
            currentUser,
            permissionChecker,
            readModelCache,
            readModelCacheOptions,
            activityStore);
    }

    public Task<WorkItemReportSnapshot<FlowTimeReportResponse>> HandleAsync(
        FlowTimeQuery query,
        CancellationToken ct) =>
        pipeline?.GetAsync(query, ct)
        ?? service.FlowTimeSnapshotAsync(query.ProjectId, query.From, query.To, ct);
}
