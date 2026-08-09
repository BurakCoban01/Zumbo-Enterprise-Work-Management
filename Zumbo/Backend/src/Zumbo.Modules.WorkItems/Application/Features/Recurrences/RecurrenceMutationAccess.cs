using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.Modules.WorkItems;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems.Application.Features.Recurrences;

internal sealed class RecurrenceMutationAccess(
    IDocumentRepository<WorkItemRecurrenceDocument> recurrences,
    IProjectPermissionChecker permissionChecker,
    ICurrentUser currentUser,
    IDistributedLockProvider distributedLocks,
    IOptions<DistributedLockOptions> lockOptions,
    IExpectedVersionAccessor? expectedVersions)
{
    private readonly ExpectedVersionState expectedVersion = new(expectedVersions);

    internal async Task<IAsyncDisposable> AcquireAsync(
        string recurrenceId,
        CancellationToken ct)
    {
        var options = lockOptions.Value;
        return await distributedLocks.TryAcquireAsync(
            "work-item-recurrence:" + recurrenceId,
            TimeSpan.FromSeconds(Math.Clamp(options.LeaseSeconds, 5, 300)),
            TimeSpan.FromSeconds(Math.Clamp(options.WaitSeconds, 0, 30)),
            ct)
        ?? throw new ConflictException(
            "RESOURCE_BUSY",
            "The requested resource is busy; retry the operation.");
    }

    internal async Task<WorkItemRecurrenceDocument> GetForUpdateAsync(
        string recurrenceId,
        CancellationToken ct)
    {
        var recurrence = await recurrences.SelectAsync(
            item => item.Id == recurrenceId && !item.Archived,
            ct)
            ?? throw new NotFoundException(
                "WORK_ITEM_RECURRENCE_NOT_FOUND",
                "Work item recurrence was not found.");
        var userId = currentUser.UserId
            ?? throw new UnauthorizedException("Authenticated user is required.");
        _ = await permissionChecker.EnsureCanAsync(
            userId,
            recurrence.ProjectId,
            PermissionCatalog.WorkItemUpdate,
            ct);
        return recurrence;
    }

    internal async Task ReplaceAsync(
        WorkItemRecurrenceDocument recurrence,
        string conflictMessage,
        CancellationToken ct)
    {
        var result = await recurrences.ReplaceByVersionAsync(
            item => item.Id == recurrence.Id,
            recurrence,
            expectedVersion.Consume(recurrence.Version),
            ct);
        if (!result.Found)
        {
            throw new ConflictException("WORK_ITEM_RECURRENCE_CONFLICT", conflictMessage);
        }
        recurrence.Version = result.Version!.Value;
    }
}
