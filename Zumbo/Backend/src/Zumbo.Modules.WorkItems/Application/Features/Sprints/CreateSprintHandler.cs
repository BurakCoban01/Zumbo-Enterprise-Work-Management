using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems.Application.Features.Sprints;

public sealed class CreateSprintHandler(SprintService service)
{
    private CreateSprintSlice? slice;

    public CreateSprintHandler(
        IDocumentRepository<SprintDocument> sprints,
        IProjectPermissionChecker permissionChecker,
        IWorkItemAuditPublisher audit,
        IDistributedLockProvider distributedLocks,
        IOptions<DistributedLockOptions> lockOptions,
        IClock clock,
        ICurrentUser currentUser,
        IWorkItemCacheInvalidationPublisher cacheInvalidationPublisher)
        : this(null!)
    {
        slice = new CreateSprintSlice(
            sprints,
            permissionChecker,
            audit,
            distributedLocks,
            lockOptions,
            clock,
            currentUser,
            cacheInvalidationPublisher);
    }

    public Task<SprintResponse> HandleAsync(CreateSprintCommand command, CancellationToken ct) =>
        slice?.HandleAsync(command, ct)
        ?? service.CreateAsync(command.Request, command.CorrelationId, ct);
}
