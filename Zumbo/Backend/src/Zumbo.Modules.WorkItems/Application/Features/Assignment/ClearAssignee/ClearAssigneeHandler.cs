using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed class ClearAssigneeHandler(WorkItemService service)
{
    private ClearAssigneeSlice? slice;

    public ClearAssigneeHandler(
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
        slice = new ClearAssigneeSlice(new AssignmentMutationPipeline(
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

    public Task<WorkItemResponse> HandleAsync(ClearAssigneeCommand command, CancellationToken ct) =>
        slice?.HandleAsync(command, ct)
        ?? service.ClearAssigneeAsync(command.Id, command.CorrelationId, ct);
}
