using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed class StatusDistributionHandler(WorkItemService service)
{
    private StatusDistributionPipeline? pipeline;

    public StatusDistributionHandler(
        IDocumentRepository<WorkItemDocument> workItems,
        ICurrentUser currentUser,
        IProjectPermissionChecker permissionChecker,
        IWorkItemReadModelCache readModelCache,
        IOptions<WorkItemReadModelCacheOptions> readModelCacheOptions)
        : this(null!)
    {
        pipeline = new StatusDistributionPipeline(
            workItems,
            currentUser,
            permissionChecker,
            readModelCache,
            readModelCacheOptions);
    }

    public Task<WorkItemReportSnapshot<IReadOnlyList<StatusDistributionResponse>>> HandleAsync(
        StatusDistributionQuery query,
        CancellationToken ct) =>
        pipeline?.GetAsync(query, ct)
        ?? service.StatusDistributionSnapshotAsync(query.ProjectId, ct);
}
