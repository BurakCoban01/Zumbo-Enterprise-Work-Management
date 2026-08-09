using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.Modules.WorkItems;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems.Application.Features.Recurrences;

internal sealed class SetWorkItemRecurrenceStateSlice(
    IDocumentRepository<WorkItemRecurrenceDocument> recurrences,
    IDocumentRepository<WorkItemRecurrenceOccurrenceDocument> occurrences,
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
    private readonly RecurrenceResponseMapper mapper = new(occurrences);

    internal async Task<WorkItemRecurrenceResponse> HandleAsync(
        SetWorkItemRecurrenceStateCommand command,
        CancellationToken ct)
    {
        await using var recurrenceLock = await access.AcquireAsync(command.RecurrenceId, ct);
        var recurrence = await access.GetForUpdateAsync(command.RecurrenceId, ct);
        if (recurrence.Active == command.Active)
        {
            throw new ConflictException(
                "WORK_ITEM_RECURRENCE_UNCHANGED",
                "The recurrence state is unchanged.");
        }
        if (command.Active
            && (recurrence.NextRunAtUtc is null
                || recurrence.ScheduledOccurrences >= recurrence.MaxOccurrences))
        {
            throw new ConflictException(
                "WORK_ITEM_RECURRENCE_COMPLETE",
                "A completed recurrence cannot be resumed.");
        }

        recurrence.Active = command.Active;
        recurrence.UpdatedAt = clock.UtcNow;
        await access.ReplaceAsync(
            recurrence,
            "The recurrence changed concurrently; reload and retry.",
            ct);
        await audit.WriteAsync(
            command.Active ? "WorkItemRecurrenceResumed" : "WorkItemRecurrencePaused",
            "WorkItemRecurrence",
            recurrence.Id,
            (!command.Active).ToString(),
            command.Active.ToString(),
            command.CorrelationId,
            ct);
        return await mapper.ToResponseAsync(recurrence, ct);
    }
}
