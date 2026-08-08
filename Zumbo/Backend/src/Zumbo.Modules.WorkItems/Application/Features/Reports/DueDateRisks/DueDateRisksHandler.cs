using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed class DueDateRisksHandler(WorkItemService service)
{
    private DueDateRisksPipeline? pipeline;

    public DueDateRisksHandler(
        IDocumentRepository<WorkItemDocument> workItems,
        IClock clock,
        ICurrentUser currentUser,
        IProjectPermissionChecker permissionChecker,
        IWorkItemReadModelCache readModelCache,
        IOptions<WorkItemReadModelCacheOptions> readModelCacheOptions)
        : this(null!)
    {
        pipeline = new DueDateRisksPipeline(
            workItems,
            clock,
            currentUser,
            permissionChecker,
            readModelCache,
            readModelCacheOptions);
    }

    public Task<WorkItemReportSnapshot<IReadOnlyList<DueDateRiskResponse>>> HandleAsync(
        DueDateRisksQuery query,
        CancellationToken ct) =>
        pipeline?.GetAsync(query, ct)
        ?? service.DueDateRisksSnapshotAsync(query.ProjectId, query.Days, ct);
}
