using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.Modules.WorkItems.Domain;
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
}
