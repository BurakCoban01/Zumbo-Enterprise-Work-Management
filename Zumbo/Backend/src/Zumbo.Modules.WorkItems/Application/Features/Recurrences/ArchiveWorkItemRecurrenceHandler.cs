using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.Modules.WorkItems;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems.Application.Features.Recurrences;

public sealed class ArchiveWorkItemRecurrenceHandler(WorkItemTemplateRecurrenceService service)
{
    private ArchiveWorkItemRecurrenceSlice? slice;

    public ArchiveWorkItemRecurrenceHandler(
        IDocumentRepository<WorkItemRecurrenceDocument> recurrences,
        IProjectPermissionChecker permissionChecker,
        ICurrentUser currentUser,
        IDistributedLockProvider distributedLocks,
        IOptions<DistributedLockOptions> lockOptions,
        IClock clock,
        IWorkItemAuditPublisher audit,
        IExpectedVersionAccessor? expectedVersions = null)
        : this(null!)
    {
        slice = new ArchiveWorkItemRecurrenceSlice(
            recurrences,
            permissionChecker,
            currentUser,
            distributedLocks,
            lockOptions,
            clock,
            audit,
            expectedVersions);
    }

    public Task HandleAsync(
        ArchiveWorkItemRecurrenceCommand command,
        CancellationToken ct) =>
        slice?.HandleAsync(command, ct)
        ?? service.ArchiveRecurrenceAsync(command.RecurrenceId, command.CorrelationId, ct);
}
