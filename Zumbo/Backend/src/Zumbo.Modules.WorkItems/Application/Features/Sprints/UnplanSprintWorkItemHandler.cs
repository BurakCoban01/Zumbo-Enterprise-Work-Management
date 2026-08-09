using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems.Application.Features.Sprints;

public sealed class UnplanSprintWorkItemHandler(SprintService service)
{
    private UnplanSprintWorkItemSlice? slice;

    public UnplanSprintWorkItemHandler(
        IDocumentRepository<SprintDocument> sprints,
        IDocumentRepository<WorkItemDocument> workItems,
        IProjectPermissionChecker permissionChecker,
        IWorkItemAuditPublisher audit,
        IDistributedLockProvider distributedLocks,
        IOptions<DistributedLockOptions> lockOptions,
        IClock clock,
        ICurrentUser currentUser,
        IWorkItemCacheInvalidationPublisher cacheInvalidationPublisher,
        IExpectedVersionAccessor? expectedVersions = null)
        : this(null!)
    {
        slice = new UnplanSprintWorkItemSlice(
            sprints,
            workItems,
            permissionChecker,
            audit,
            distributedLocks,
            lockOptions,
            clock,
            currentUser,
            cacheInvalidationPublisher,
            expectedVersions);
    }

    public Task<SprintPlannedItemResponse> HandleAsync(
        UnplanSprintWorkItemCommand command,
        CancellationToken ct) =>
        slice?.HandleAsync(command, ct)
        ?? service.UnplanAsync(
            command.SprintId,
            command.WorkItemId,
            command.CorrelationId,
            ct);
}
