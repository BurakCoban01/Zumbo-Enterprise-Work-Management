using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed class UnlinkWorkItemHandler(WorkItemService service)
{
    private UnlinkWorkItemSlice? slice;

    public UnlinkWorkItemHandler(
        IDocumentRepository<WorkItemDocument> workItems,
        IWorkItemAuditPublisher audit,
        IClock clock,
        ICurrentUser currentUser,
        IProjectPermissionChecker permissionChecker,
        IDistributedLockProvider distributedLockProvider,
        IOptions<DistributedLockOptions> distributedLockOptions,
        IWorkItemActivityStore activityStore,
        WorkItemGraphService graph,
        IExpectedVersionAccessor? expectedVersions,
        WorkItemCollaborationService? collaborationService)
        : this(null!)
    {
        slice = new UnlinkWorkItemSlice(
            new UnlinkWorkItemPipeline(
                workItems,
                audit,
                clock,
                currentUser,
                permissionChecker,
                distributedLockProvider,
                distributedLockOptions,
                activityStore,
                graph,
                expectedVersions,
                collaborationService));
    }

    public Task<WorkItemResponse> HandleAsync(UnlinkWorkItemCommand command, CancellationToken ct) =>
        slice?.HandleAsync(command, ct)
        ?? service.UnlinkAsync(
            command.Id,
            command.RelatedWorkItemId,
            command.RelationType,
            command.CorrelationId,
            ct);
}
