using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed class AddWorkLogHandler(WorkItemService service)
{
    private AddWorkLogSlice? slice;

    public AddWorkLogHandler(
        IDocumentRepository<WorkItemDocument> workItems,
        IClock clock,
        ICurrentUser currentUser,
        IProjectPermissionChecker permissionChecker,
        IWorkItemActivityStore activityStore,
        IExpectedVersionAccessor? expectedVersions,
        IWorkItemCacheInvalidationPublisher cacheInvalidationPublisher,
        WorkItemCollaborationService? collaborationService)
        : this(null!)
    {
        slice = new AddWorkLogSlice(new WorkLogMutationPipeline(
            workItems,
            clock,
            currentUser,
            permissionChecker,
            activityStore,
            expectedVersions,
            cacheInvalidationPublisher,
            collaborationService));
    }

    public Task<WorkItemResponse> HandleAsync(AddWorkLogCommand command, CancellationToken ct) =>
        slice?.HandleAsync(command, ct)
        ?? service.AddWorkLogAsync(command.Id, command.Request, ct);
}
