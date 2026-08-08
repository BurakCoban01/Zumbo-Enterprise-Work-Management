using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed class ReorderWorkItemHandler(WorkItemService service)
{
    private ReorderWorkItemSlice? slice;

    public ReorderWorkItemHandler(
        IDocumentRepository<WorkItemDocument> workItems,
        IWorkItemAuditPublisher audit,
        IClock clock,
        ICurrentUser currentUser,
        IProjectPermissionChecker permissionChecker,
        IDistributedLockProvider distributedLockProvider,
        IOptions<DistributedLockOptions> distributedLockOptions,
        IWorkItemRealtimePublisher realtimePublisher,
        IWorkItemActivityStore activityStore,
        IExpectedVersionAccessor? expectedVersions,
        WorkItemRankService ranks,
        WorkItemCollaborationService? collaborationService)
        : this(null!)
    {
        slice = new ReorderWorkItemSlice(
            new ReorderWorkItemPipeline(
                workItems,
                audit,
                clock,
                currentUser,
                permissionChecker,
                distributedLockProvider,
                distributedLockOptions,
                realtimePublisher,
                activityStore,
                expectedVersions,
                collaborationService),
            ranks);
    }

    public Task<WorkItemResponse> HandleAsync(ReorderWorkItemCommand command, CancellationToken ct) =>
        slice?.HandleAsync(command, ct)
        ?? service.ReorderAsync(command.Id, command.Request, command.CorrelationId, ct);
}
