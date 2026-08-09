using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems.Application.Features.Sprints;

public sealed class CompleteSprintHandler(SprintService service)
{
    private CompleteSprintSlice? slice;

    public CompleteSprintHandler(
        IDocumentRepository<SprintDocument> sprints,
        IDocumentRepository<SprintScopeSnapshotDocument> scopeSnapshots,
        IDocumentRepository<SprintCompletionSnapshotDocument> completionSnapshots,
        IDocumentRepository<WorkItemDocument> workItems,
        IProjectPermissionChecker permissionChecker,
        IWorkItemAuditPublisher audit,
        IDistributedLockProvider distributedLocks,
        IOptions<DistributedLockOptions> lockOptions,
        IOptions<SprintOptions> configuredOptions,
        IClock clock,
        ICurrentUser currentUser,
        IWorkItemCacheInvalidationPublisher cacheInvalidationPublisher,
        IExpectedVersionAccessor? expectedVersions = null)
        : this(null!)
    {
        slice = new CompleteSprintSlice(
            sprints,
            scopeSnapshots,
            completionSnapshots,
            workItems,
            permissionChecker,
            audit,
            distributedLocks,
            lockOptions,
            configuredOptions,
            clock,
            currentUser,
            cacheInvalidationPublisher,
            expectedVersions);
    }

    public Task<SprintResponse> HandleAsync(CompleteSprintCommand command, CancellationToken ct) =>
        slice?.HandleAsync(command, ct)
        ?? service.CompleteAsync(command.SprintId, command.Request, command.CorrelationId, ct);
}
