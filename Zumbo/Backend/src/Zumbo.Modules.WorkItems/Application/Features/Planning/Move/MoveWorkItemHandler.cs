using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed class MoveWorkItemHandler(WorkItemService service)
{
    private MoveWorkItemSlice? slice;

    public MoveWorkItemHandler(
        IDocumentRepository<WorkItemDocument> workItems,
        IWorkItemAuditPublisher audit,
        IClock clock,
        ICurrentUser currentUser,
        IProjectPermissionChecker permissionChecker,
        IWorkflowPolicy workflowPolicy,
        IBoardPlacementPolicy boardPlacementPolicy,
        IDistributedLockProvider distributedLockProvider,
        IOptions<DistributedLockOptions> distributedLockOptions,
        IWorkItemSearchPublisher searchPublisher,
        IWorkItemRealtimePublisher realtimePublisher,
        IWorkItemCacheInvalidationPublisher cacheInvalidationPublisher,
        IWorkItemActivityStore activityStore,
        WorkItemGraphService graph,
        IExpectedVersionAccessor? expectedVersions,
        WorkItemWipProjection? wipProjection,
        WorkItemRankService ranks,
        WorkItemCollaborationService? collaborationService,
        IWorkItemAutomationEventPublisher? automationEvents,
        IWorkItemAutomationChainContextAccessor? automationChain)
        : this(null!)
    {
        var pipeline = new MoveWorkItemPipeline(
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
            graph,
            expectedVersions,
            wipProjection,
            collaborationService,
            automationEvents,
            automationChain);
        slice = new MoveWorkItemSlice(
            pipeline,
            workflowPolicy,
            boardPlacementPolicy,
            ranks);
    }

    public Task<WorkItemResponse> HandleAsync(MoveWorkItemCommand command, CancellationToken ct) =>
        slice?.HandleAsync(command, ct)
        ?? service.MoveAsync(command.Id, command.Request, command.CorrelationId, ct);
}
