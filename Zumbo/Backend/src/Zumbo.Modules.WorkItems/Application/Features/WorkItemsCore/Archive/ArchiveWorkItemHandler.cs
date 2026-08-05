using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed class ArchiveWorkItemHandler(WorkItemService service)
{
    private ArchiveWorkItemSlice? slice;

    public ArchiveWorkItemHandler(
        IDocumentRepository<WorkItemDocument> workItems,
        IWorkItemAuditPublisher audit,
        IClock clock,
        ICurrentUser currentUser,
        IProjectPermissionChecker permissionChecker,
        IDistributedLockProvider distributedLockProvider,
        IOptions<DistributedLockOptions> distributedLockOptions,
        IWorkItemSearchPublisher searchPublisher,
        IWorkItemRealtimePublisher realtimePublisher,
        IWorkItemCacheInvalidationPublisher cacheInvalidationPublisher,
        IWorkItemActivityStore activityStore,
        IExpectedVersionAccessor? expectedVersions,
        WorkItemWipProjection? wipProjection,
        WorkItemCollaborationService? collaborationService)
        : this(null!)
    {
        slice = new ArchiveWorkItemSlice(
            workItems,
            audit,
            clock,
            currentUser,
            permissionChecker,
            distributedLockProvider,
            distributedLockOptions,
            searchPublisher,
            realtimePublisher,
            cacheInvalidationPublisher,
            activityStore,
            expectedVersions,
            wipProjection,
            collaborationService);
    }

    public Task HandleAsync(ArchiveWorkItemCommand command, CancellationToken ct) =>
        slice?.HandleAsync(command, ct)
        ?? service.ArchiveAsync(command.Id, command.CorrelationId, ct);
}
