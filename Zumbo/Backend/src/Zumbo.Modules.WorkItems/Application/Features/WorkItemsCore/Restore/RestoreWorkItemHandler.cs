using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed class RestoreWorkItemHandler(WorkItemService service)
{
    private RestoreWorkItemSlice? slice;

    public RestoreWorkItemHandler(
        IDocumentRepository<WorkItemDocument> workItems,
        IWorkItemAuditPublisher audit,
        IClock clock,
        ICurrentUser currentUser,
        IProjectPermissionChecker permissionChecker,
        IBoardPlacementPolicy boardPlacementPolicy,
        IDistributedLockProvider distributedLockProvider,
        IOptions<DistributedLockOptions> distributedLockOptions,
        IWorkItemSearchPublisher searchPublisher,
        IWorkItemRealtimePublisher realtimePublisher,
        IWorkItemCacheInvalidationPublisher cacheInvalidationPublisher,
        IWorkItemActivityStore activityStore,
        IExpectedVersionAccessor? expectedVersions,
        WorkItemWipProjection? wipProjection,
        WorkItemRankService ranks,
        WorkItemCollaborationService? collaborationService)
        : this(null!)
    {
        slice = new RestoreWorkItemSlice(
            workItems,
            audit,
            clock,
            currentUser,
            permissionChecker,
            boardPlacementPolicy,
            distributedLockProvider,
            distributedLockOptions,
            searchPublisher,
            realtimePublisher,
            cacheInvalidationPublisher,
            activityStore,
            expectedVersions,
            wipProjection,
            ranks,
            collaborationService);
    }

    public Task<WorkItemResponse> HandleAsync(RestoreWorkItemCommand command, CancellationToken ct) =>
        slice?.HandleAsync(command, ct)
        ?? service.RestoreAsync(command.Id, command.CorrelationId, ct);
}
