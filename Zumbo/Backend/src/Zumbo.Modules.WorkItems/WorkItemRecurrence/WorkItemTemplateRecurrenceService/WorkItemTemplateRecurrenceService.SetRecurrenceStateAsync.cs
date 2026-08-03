using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed partial class WorkItemTemplateRecurrenceService{

    public async Task<WorkItemRecurrenceResponse> SetRecurrenceStateAsync(
        string recurrenceId,
        bool active,
        string correlationId,
        CancellationToken ct)
    {
        await using var recurrenceLock = await AcquireAsync("work-item-recurrence:" + recurrenceId, ct);
        var recurrence = await GetRecurrenceAsync(recurrenceId, includeArchived: false, ct);
        await EnsurePermissionAsync(recurrence.ProjectId, PermissionCatalog.WorkItemUpdate, ct);
        if (recurrence.Active == active)
        {
            throw new ConflictException("WORK_ITEM_RECURRENCE_UNCHANGED", "The recurrence state is unchanged.");
        }
        if (active && (recurrence.NextRunAtUtc is null || recurrence.ScheduledOccurrences >= recurrence.MaxOccurrences))
        {
            throw new ConflictException("WORK_ITEM_RECURRENCE_COMPLETE", "A completed recurrence cannot be resumed.");
        }

        recurrence.Active = active;
        recurrence.UpdatedAt = clock.UtcNow;
        var result = await recurrences.ReplaceByVersionAsync(
            x => x.Id == recurrence.Id,
            recurrence,
            expectedVersion.Consume(recurrence.Version),
            ct);
        if (!result.Found)
        {
            throw new ConflictException("WORK_ITEM_RECURRENCE_CONFLICT", "The recurrence changed concurrently; reload and retry.");
        }
        recurrence.Version = result.Version!.Value;
        await audit.WriteAsync(
            active ? "WorkItemRecurrenceResumed" : "WorkItemRecurrencePaused",
            "WorkItemRecurrence", recurrence.Id, (!active).ToString(), active.ToString(), correlationId, ct);
        return await ToResponseAsync(recurrence, ct);
    }
}
