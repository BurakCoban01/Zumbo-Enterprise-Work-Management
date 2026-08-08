using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed class AddCommentHandler(WorkItemService service)
{
    private AddCommentSlice? slice;

    public AddCommentHandler(
        IDocumentRepository<WorkItemDocument> workItems,
        IWorkItemNotificationPublisher notifications,
        IWorkItemAuditPublisher audit,
        IClock clock,
        ICurrentUser currentUser,
        IProjectPermissionChecker permissionChecker,
        IWorkItemActivityStore activityStore,
        IExpectedVersionAccessor? expectedVersions,
        WorkItemCollaborationService? collaborationService,
        IWorkItemAutomationEventPublisher? automationEvents,
        IWorkItemAutomationChainContextAccessor? automationChain)
        : this(null!)
    {
        slice = new AddCommentSlice(
            new AddCommentPipeline(
                workItems,
                notifications,
                audit,
                clock,
                currentUser,
                permissionChecker,
                activityStore,
                expectedVersions,
                collaborationService,
                automationEvents,
                automationChain));
    }

    public Task<WorkItemResponse> HandleAsync(AddCommentCommand command, CancellationToken ct) =>
        slice?.HandleAsync(command, ct)
        ?? service.AddCommentAsync(command.Id, command.Request, command.CorrelationId, ct);
}
