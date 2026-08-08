using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed class SetParentHandler(WorkItemService service)
{
    private SetParentSlice? slice;

    public SetParentHandler(
        IDocumentRepository<WorkItemDocument> workItems,
        IWorkItemAuditPublisher audit,
        IClock clock,
        ICurrentUser currentUser,
        IProjectPermissionChecker permissionChecker,
        IWorkItemTypeSchemaPolicy typeSchemas,
        IDistributedLockProvider distributedLockProvider,
        IOptions<DistributedLockOptions> distributedLockOptions,
        IWorkItemActivityStore activityStore,
        WorkItemGraphService graph,
        IExpectedVersionAccessor? expectedVersions,
        WorkItemCollaborationService? collaborationService)
        : this(null!)
    {
        slice = new SetParentSlice(
            new SetParentPipeline(
                workItems,
                audit,
                clock,
                currentUser,
                permissionChecker,
                typeSchemas,
                distributedLockProvider,
                distributedLockOptions,
                activityStore,
                graph,
                expectedVersions,
                collaborationService));
    }

    public Task<WorkItemResponse> HandleAsync(SetParentCommand command, CancellationToken ct) =>
        slice?.HandleAsync(command, ct)
        ?? service.SetParentAsync(command.Id, command.Request, command.CorrelationId, ct);
}
