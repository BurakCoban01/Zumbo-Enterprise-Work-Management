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

    public async Task<WorkItemRecurrenceOccurrencePage> ListOccurrencesAsync(
        string recurrenceId,
        int page,
        int pageSize,
        CancellationToken ct)
    {
        var recurrence = await GetRecurrenceAsync(recurrenceId, includeArchived: true, ct);
        await EnsurePermissionAsync(recurrence.ProjectId, PermissionCatalog.WorkItemView, ct);
        var safePage = Math.Max(page, 1);
        var safeSize = Math.Clamp(pageSize, 1, 100);
        var total = await occurrences.CountByFilterAsync(x => x.RecurrenceId == recurrence.Id, ct);
        var result = await occurrences.ListByFilterAsync(
            x => x.RecurrenceId == recurrence.Id,
            x => x.ScheduledForUtc,
            orderDescending: true,
            page: safePage,
            pageSize: safeSize,
            cancellationToken: ct);
        return new WorkItemRecurrenceOccurrencePage(
            result.Select(ToResponse).ToList(), safePage, safeSize, total);
    }
}
