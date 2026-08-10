using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems.Application.Features.Sprints;

public sealed class StartSprintHandler(SprintService service)
{
    private StartSprintSlice? slice;

    public StartSprintHandler(
        IDocumentRepository<SprintDocument> sprints,
        IDocumentRepository<SprintScopeSnapshotDocument> scopeSnapshots,
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
        slice = new StartSprintSlice(
            sprints,
            scopeSnapshots,
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

    public Task<SprintResponse> HandleAsync(StartSprintCommand command, CancellationToken ct) =>
        slice?.HandleAsync(command, ct)
        ?? service.StartAsync(command.SprintId, command.CorrelationId, ct);
}
