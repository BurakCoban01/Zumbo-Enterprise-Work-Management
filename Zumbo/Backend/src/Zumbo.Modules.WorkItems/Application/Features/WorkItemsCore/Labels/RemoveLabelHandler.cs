using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed class RemoveLabelHandler(WorkItemService service)
{
    private RemoveLabelSlice? slice;

    public RemoveLabelHandler(
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
        slice = new RemoveLabelSlice(new LabelMutationPipeline(
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

    public Task<WorkItemResponse> HandleAsync(RemoveLabelCommand command, CancellationToken ct) =>
        slice?.HandleAsync(command, ct)
        ?? service.RemoveLabelAsync(command.Id, command.Label, ct);
}
