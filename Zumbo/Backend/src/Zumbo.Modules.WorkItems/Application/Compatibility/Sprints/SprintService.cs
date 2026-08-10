using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.Modules.WorkItems.Domain;
using Zumbo.Modules.WorkItems.Application.Features.Sprints;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed partial class SprintService(
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
    IWorkItemReadModelCache readModelCache,
    IWorkItemCacheInvalidationPublisher cacheInvalidationPublisher,
    IOptions<WorkItemReadModelCacheOptions> readModelCacheOptions,
    IExpectedVersionAccessor? expectedVersions = null)
{
    private int BatchSize => Math.Clamp(configuredOptions.Value.BatchSize, 1, 200);
    private int MaxBatches => Math.Clamp(configuredOptions.Value.MaxBatchesPerOperation, 1, 10_000);
    private TimeSpan ReadModelTtl => TimeSpan.FromSeconds(
        Math.Clamp(readModelCacheOptions.Value.TtlSeconds, 1, 300));
    private readonly GetSprintHandler getSprintHandler =
        new(sprints, workItems, permissionChecker, currentUser);
    private readonly ListSprintsHandler listSprintsHandler =
        new(sprints, workItems, permissionChecker, currentUser);
    private readonly ListSprintBacklogHandler listSprintBacklogHandler =
        new(sprints, workItems, permissionChecker, currentUser);
    private readonly GetSprintBurndownHandler getSprintBurndownHandler =
        new(sprints, scopeSnapshots, completionSnapshots, workItems, permissionChecker, currentUser,
            configuredOptions, readModelCache, readModelCacheOptions);
    private readonly GetSprintVelocityHandler getSprintVelocityHandler =
        new(sprints, permissionChecker, currentUser, readModelCache, readModelCacheOptions);
    private readonly CreateSprintHandler createSprintHandler =
        new(sprints, permissionChecker, audit, distributedLocks, lockOptions, clock, currentUser,
            cacheInvalidationPublisher);
    private readonly StartSprintHandler startSprintHandler =
        new(sprints, scopeSnapshots, workItems, permissionChecker, audit, distributedLocks, lockOptions,
            configuredOptions, clock, currentUser, cacheInvalidationPublisher, expectedVersions);
    private readonly CompleteSprintHandler completeSprintHandler =
        new(sprints, scopeSnapshots, completionSnapshots, workItems, permissionChecker, audit, distributedLocks,
            lockOptions, configuredOptions, clock, currentUser, cacheInvalidationPublisher, expectedVersions);
    private readonly PlanSprintWorkItemHandler planSprintWorkItemHandler =
        new(sprints, workItems, permissionChecker, audit, distributedLocks, lockOptions, clock, currentUser,
            cacheInvalidationPublisher, expectedVersions);
    private readonly UnplanSprintWorkItemHandler unplanSprintWorkItemHandler =
        new(sprints, workItems, permissionChecker, audit, distributedLocks, lockOptions, clock, currentUser,
            cacheInvalidationPublisher, expectedVersions);
}
