using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed class UserWorkloadHandler(WorkItemService service)
{
    private UserWorkloadPipeline? pipeline;

    public UserWorkloadHandler(
        IDocumentRepository<WorkItemDocument> workItems,
        IClock clock,
        ICurrentUser currentUser,
        IProjectPermissionChecker permissionChecker,
        IWorkItemReadModelCache readModelCache,
        IOptions<WorkItemReadModelCacheOptions> readModelCacheOptions,
        IWorkItemActivityStore activityStore)
        : this(null!)
    {
        pipeline = new UserWorkloadPipeline(
            workItems,
            clock,
            currentUser,
            permissionChecker,
            readModelCache,
            readModelCacheOptions,
            activityStore);
    }

    public Task<WorkItemReportSnapshot<IReadOnlyList<UserWorkloadResponse>>> HandleAsync(
        UserWorkloadQuery query,
        CancellationToken ct) =>
        pipeline?.GetAsync(query, ct)
        ?? service.UserWorkloadSnapshotAsync(query.ProjectId, ct);
}
