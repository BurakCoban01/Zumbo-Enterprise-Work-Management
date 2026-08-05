using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed class SetPlanningHandler(WorkItemService service)
{
    private SetPlanningSlice? slice;

    public SetPlanningHandler(
        IDocumentRepository<WorkItemDocument> workItems,
        IClock clock,
        ICurrentUser currentUser,
        IProjectPermissionChecker permissionChecker,
        IWorkItemSprintPolicy? sprintPolicy,
        IWorkItemSearchPublisher searchPublisher,
        IWorkItemActivityStore activityStore,
        IExpectedVersionAccessor? expectedVersions,
        IWorkItemCacheInvalidationPublisher cacheInvalidationPublisher,
        WorkItemCollaborationService? collaborationService)
        : this(null!)
    {
        slice = new SetPlanningSlice(
            new SetPlanningPipeline(
                workItems,
                currentUser,
                permissionChecker,
                searchPublisher,
                activityStore,
                expectedVersions,
                cacheInvalidationPublisher,
                collaborationService),
            clock,
            sprintPolicy ?? new NoOpWorkItemSprintPolicy());
    }

    public Task<WorkItemResponse> HandleAsync(SetPlanningCommand command, CancellationToken ct) =>
        slice?.HandleAsync(command, ct)
        ?? service.SetPlanningAsync(command.Id, command.Request, ct);
}
