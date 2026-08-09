using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.Modules.WorkItems;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems.Application.Features.Recurrences;

internal sealed class ArchiveWorkItemRecurrenceSlice(
    IDocumentRepository<WorkItemRecurrenceDocument> recurrences,
    IProjectPermissionChecker permissionChecker,
    ICurrentUser currentUser,
    IDistributedLockProvider distributedLocks,
    IOptions<DistributedLockOptions> lockOptions,
    IClock clock,
    IWorkItemAuditPublisher audit,
    IExpectedVersionAccessor? expectedVersions)
{
    private readonly RecurrenceMutationAccess access = new(
        recurrences,
        permissionChecker,
        currentUser,
        distributedLocks,
        lockOptions,
        expectedVersions);

    internal async Task HandleAsync(
        ArchiveWorkItemRecurrenceCommand command,
        CancellationToken ct)
    {
        await using var recurrenceLock = await access.AcquireAsync(command.RecurrenceId, ct);
        var recurrence = await access.GetForUpdateAsync(command.RecurrenceId, ct);
        recurrence.Active = false;
        recurrence.Archived = true;
        recurrence.UpdatedAt = clock.UtcNow;
        await access.ReplaceAsync(
            recurrence,
            "The recurrence changed concurrently; reload and retry.",
            ct);
        await audit.WriteAsync(
            "WorkItemRecurrenceArchived",
            "WorkItemRecurrence",
            recurrence.Id,
            "active",
            "archived",
            command.CorrelationId,
            ct);
    }
}
