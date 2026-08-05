using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed class DecideApprovalHandler(WorkItemService service)
{
    private DecideApprovalSlice? slice;

    public DecideApprovalHandler(
        IDocumentRepository<WorkItemDocument> workItems,
        IWorkItemNotificationPublisher notifications,
        IWorkItemAuditPublisher audit,
        IClock clock,
        ICurrentUser currentUser,
        IProjectPermissionChecker permissionChecker,
        IWorkItemActivityStore activityStore,
        IExpectedVersionAccessor? expectedVersions,
        WorkItemCollaborationService? collaborationService)
        : this(null!)
    {
        slice = new DecideApprovalSlice(
            new ApprovalMutationPipeline(
                workItems,
                notifications,
                audit,
                clock,
                currentUser,
                permissionChecker,
                activityStore,
                expectedVersions,
                collaborationService));
    }

    public Task<WorkItemResponse> HandleAsync(DecideApprovalCommand command, CancellationToken ct) =>
        slice?.HandleAsync(command, ct)
        ?? service.DecideApprovalAsync(
            command.Id,
            command.ApprovalId,
            command.Request,
            command.CorrelationId,
            ct);
}
