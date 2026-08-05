using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed class UpdateWorkItemHandler(WorkItemService service)
{
    private UpdateWorkItemSlice? slice;

    public UpdateWorkItemHandler(
        IDocumentRepository<WorkItemDocument> workItems,
        IWorkItemAuditPublisher audit,
        IClock clock,
        ICurrentUser currentUser,
        IProjectPermissionChecker permissionChecker,
        IWorkItemSearchPublisher searchPublisher,
        IWorkItemRealtimePublisher realtimePublisher,
        IWorkItemCacheInvalidationPublisher cacheInvalidationPublisher,
        IWorkItemActivityStore activityStore,
        IExpectedVersionAccessor? expectedVersions,
        WorkItemCollaborationService? collaborationService,
        IWorkItemAutomationEventPublisher? automationEvents,
        IWorkItemAutomationChainContextAccessor? automationChain)
        : this(null!)
    {
        slice = new UpdateWorkItemSlice(new UpdateWorkItemPipeline(
            workItems,
            audit,
            clock,
            currentUser,
            permissionChecker,
            searchPublisher,
            realtimePublisher,
            cacheInvalidationPublisher,
            activityStore,
            expectedVersions,
            collaborationService,
            automationEvents,
            automationChain));
    }

    public Task<WorkItemResponse> HandleAsync(UpdateWorkItemCommand command, CancellationToken ct) =>
        slice?.HandleAsync(command, ct)
        ?? service.UpdateAsync(command.Id, command.Request, command.CorrelationId, ct);
}
