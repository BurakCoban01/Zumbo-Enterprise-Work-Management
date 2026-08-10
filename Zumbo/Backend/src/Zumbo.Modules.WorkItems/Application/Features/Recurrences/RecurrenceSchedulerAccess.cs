using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.Modules.WorkItems;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems.Application.Features.Recurrences;

internal sealed class RecurrenceSchedulerAccess(
    IDocumentRepository<WorkItemRecurrenceDocument> recurrences,
    IDistributedLockProvider distributedLocks,
    IOptions<DistributedLockOptions> lockOptions)
{
    internal async Task<IAsyncDisposable> AcquireAsync(string resource, CancellationToken ct)
    {
        var options = lockOptions.Value;
        return await distributedLocks.TryAcquireAsync(
            resource,
            TimeSpan.FromSeconds(Math.Clamp(options.LeaseSeconds, 5, 300)),
            TimeSpan.FromSeconds(Math.Clamp(options.WaitSeconds, 0, 30)),
            ct)
        ?? throw new ConflictException(
            "RESOURCE_BUSY",
            "The requested resource is busy; retry the operation.");
    }

    internal async Task ReplaceAsync(
        WorkItemRecurrenceDocument recurrence,
        CancellationToken ct)
    {
        var result = await recurrences.ReplaceByVersionAsync(
            item => item.Id == recurrence.Id,
            recurrence,
            recurrence.Version,
            ct);
        if (!result.Found)
        {
            throw new ConflictException(
                "WORK_ITEM_RECURRENCE_CONFLICT",
                "The recurrence changed concurrently; retry the operation.");
        }
        recurrence.Version = result.Version!.Value;
    }
}
