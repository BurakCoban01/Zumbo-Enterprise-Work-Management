using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed class AddLabelHandler(WorkItemService service)
{
    private AddLabelSlice? slice;

    public AddLabelHandler(
        IDocumentRepository<WorkItemDocument> workItems,
        IClock clock,
        ICurrentUser currentUser,
        IProjectPermissionChecker permissionChecker,
        IWorkItemSearchPublisher searchPublisher,
        IWorkItemActivityStore activityStore,
        IExpectedVersionAccessor? expectedVersions,
        WorkItemCollaborationService? collaborationService,
        IWorkItemAutomationEventPublisher? automationEvents,
        IWorkItemAutomationChainContextAccessor? automationChain)
        : this(null!)
    {
        slice = new AddLabelSlice(new LabelMutationPipeline(
            workItems,
            clock,
            currentUser,
            permissionChecker,
            searchPublisher,
            activityStore,
            expectedVersions,
            collaborationService,
            automationEvents,
            automationChain));
    }

    public Task<WorkItemResponse> HandleAsync(AddLabelCommand command, CancellationToken ct) =>
        slice?.HandleAsync(command, ct)
        ?? service.AddLabelAsync(command.Id, command.Request, ct);
}
