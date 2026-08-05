using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed class RequestApprovalHandler(WorkItemService service)
{
    private RequestApprovalSlice? slice;

    public RequestApprovalHandler(
        IDocumentRepository<WorkItemDocument> workItems,
        IWorkItemNotificationPublisher notifications,
        IWorkItemAuditPublisher audit,
        IClock clock,
        ICurrentUser currentUser,
        IProjectPermissionChecker permissionChecker,
        IWorkflowPolicy workflowPolicy,
        IWorkItemActivityStore activityStore,
        IExpectedVersionAccessor? expectedVersions,
        WorkItemCollaborationService? collaborationService)
        : this(null!)
    {
        slice = new RequestApprovalSlice(
            new ApprovalMutationPipeline(
                workItems,
                notifications,
                audit,
                clock,
                currentUser,
                permissionChecker,
                activityStore,
                expectedVersions,
                collaborationService),
            workflowPolicy);
    }

    public Task<WorkItemResponse> HandleAsync(RequestApprovalCommand command, CancellationToken ct) =>
        slice?.HandleAsync(command, ct)
        ?? service.RequestApprovalAsync(command.Id, command.Request, command.CorrelationId, ct);
}
