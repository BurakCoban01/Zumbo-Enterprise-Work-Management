using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed class CompletionRateHandler(WorkItemService service)
{
    private CompletionRatePipeline? pipeline;

    public CompletionRateHandler(
        IDocumentRepository<WorkItemDocument> workItems,
        IClock clock,
        ICurrentUser currentUser,
        IProjectPermissionChecker permissionChecker,
        IWorkItemReadModelCache readModelCache,
        IOptions<WorkItemReadModelCacheOptions> readModelCacheOptions)
        : this(null!)
    {
        pipeline = new CompletionRatePipeline(
            workItems,
            clock,
            currentUser,
            permissionChecker,
            readModelCache,
            readModelCacheOptions);
    }

    public Task<WorkItemReportSnapshot<TaskCompletionRateResponse>> HandleAsync(
        CompletionRateQuery query,
        CancellationToken ct) =>
        pipeline?.GetAsync(query, ct)
        ?? service.CompletionRateSnapshotAsync(query.ProjectId, query.From, query.To, ct);
}
