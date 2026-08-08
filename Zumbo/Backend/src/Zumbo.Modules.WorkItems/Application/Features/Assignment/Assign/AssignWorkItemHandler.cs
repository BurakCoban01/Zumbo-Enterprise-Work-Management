using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed class AssignWorkItemHandler(WorkItemService service)
{
    private AssignWorkItemSlice? slice;

    public AssignWorkItemHandler(
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
        IWorkItemTeamPolicy teamPolicy,
        IWorkItemNotificationPublisher notifications)
        : this(null!)
    {
        slice = new AssignWorkItemSlice(
            new AssignmentMutationPipeline(
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
                null,
                null),
            teamPolicy,
            notifications);
    }

    public Task<WorkItemResponse> HandleAsync(AssignWorkItemCommand command, CancellationToken ct) =>
        slice?.HandleAsync(command, ct)
        ?? service.AssignAsync(command.Id, command.Request, command.CorrelationId, ct);
}
