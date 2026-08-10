using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems.Application.Features.Sprints;

public sealed class PlanSprintWorkItemHandler(SprintService service)
{
    private PlanSprintWorkItemSlice? slice;

    public PlanSprintWorkItemHandler(
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
        slice = new PlanSprintWorkItemSlice(
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
        PlanSprintWorkItemCommand command,
        CancellationToken ct) =>
        slice?.HandleAsync(command, ct)
        ?? service.PlanAsync(
            command.SprintId,
            command.WorkItemId,
            command.Request,
            command.CorrelationId,
            ct);
}
