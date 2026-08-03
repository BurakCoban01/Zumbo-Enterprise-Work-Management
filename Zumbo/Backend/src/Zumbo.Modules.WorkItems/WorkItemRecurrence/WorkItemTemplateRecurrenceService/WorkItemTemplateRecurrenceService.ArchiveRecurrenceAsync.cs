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

    public async Task ArchiveRecurrenceAsync(string recurrenceId, string correlationId, CancellationToken ct)
    {
        await using var recurrenceLock = await AcquireAsync("work-item-recurrence:" + recurrenceId, ct);
        var recurrence = await GetRecurrenceAsync(recurrenceId, includeArchived: false, ct);
        await EnsurePermissionAsync(recurrence.ProjectId, PermissionCatalog.WorkItemUpdate, ct);
        recurrence.Active = false;
        recurrence.Archived = true;
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
        await audit.WriteAsync(
            "WorkItemRecurrenceArchived", "WorkItemRecurrence", recurrence.Id, "active", "archived", correlationId, ct);
    }
}
