using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed class AddChecklistItemHandler(WorkItemService service)
{
    private AddChecklistItemSlice? slice;

    public AddChecklistItemHandler(
        IDocumentRepository<WorkItemDocument> workItems,
        IClock clock,
        ICurrentUser currentUser,
        IProjectPermissionChecker permissionChecker,
        IWorkItemActivityStore activityStore,
        IExpectedVersionAccessor? expectedVersions,
        WorkItemCollaborationService? collaborationService)
        : this(null!)
    {
        slice = new AddChecklistItemSlice(new ChecklistMutationPipeline(
            workItems,
            clock,
            currentUser,
            permissionChecker,
            activityStore,
            expectedVersions,
            collaborationService));
    }

    public Task<WorkItemResponse> HandleAsync(AddChecklistItemCommand command, CancellationToken ct) =>
        slice?.HandleAsync(command, ct)
        ?? service.AddChecklistItemAsync(command.Id, command.Request, ct);
}
