using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed class SetWorkItemTeamHandler(WorkItemService service)
{
    private SetWorkItemTeamSlice? slice;

    public SetWorkItemTeamHandler(
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
        IWorkItemTeamPolicy teamPolicy)
        : this(null!)
    {
        slice = new SetWorkItemTeamSlice(
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
            teamPolicy);
    }

    public Task<WorkItemResponse> HandleAsync(SetWorkItemTeamCommand command, CancellationToken ct) =>
        slice?.HandleAsync(command, ct)
        ?? service.SetTeamAsync(command.Id, command.Request, command.CorrelationId, ct);
}
