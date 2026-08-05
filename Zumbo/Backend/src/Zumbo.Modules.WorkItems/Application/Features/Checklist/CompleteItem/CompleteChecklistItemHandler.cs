using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed class CompleteChecklistItemHandler(WorkItemService service)
{
    private CompleteChecklistItemSlice? slice;

    public CompleteChecklistItemHandler(
        IDocumentRepository<WorkItemDocument> workItems,
        IClock clock,
        ICurrentUser currentUser,
        IProjectPermissionChecker permissionChecker,
        IWorkItemActivityStore activityStore,
        IExpectedVersionAccessor? expectedVersions,
        WorkItemCollaborationService? collaborationService)
        : this(null!)
    {
        slice = new CompleteChecklistItemSlice(new ChecklistMutationPipeline(
            workItems,
            clock,
            currentUser,
            permissionChecker,
            activityStore,
            expectedVersions,
            collaborationService));
    }

    public Task<WorkItemResponse> HandleAsync(
        CompleteChecklistItemCommand command,
        CancellationToken ct) =>
        slice?.HandleAsync(command, ct)
        ?? service.CompleteChecklistItemAsync(command.Id, command.ItemId, command.Request, ct);
}
