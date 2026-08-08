using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed class LinkWorkItemHandler(WorkItemService service)
{
    private LinkWorkItemSlice? slice;

    public LinkWorkItemHandler(
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
        slice = new LinkWorkItemSlice(
            new LinkWorkItemPipeline(
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

    public Task<WorkItemResponse> HandleAsync(LinkWorkItemCommand command, CancellationToken ct) =>
        slice?.HandleAsync(command, ct)
        ?? service.LinkAsync(command.Id, command.Request, command.CorrelationId, ct);
}
