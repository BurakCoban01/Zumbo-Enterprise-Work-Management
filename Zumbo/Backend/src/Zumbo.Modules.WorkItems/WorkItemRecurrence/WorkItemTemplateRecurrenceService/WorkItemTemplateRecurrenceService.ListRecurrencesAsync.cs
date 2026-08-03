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

    public async Task<WorkItemRecurrencePage> ListRecurrencesAsync(
        string projectId,
        int page,
        int pageSize,
        bool includeArchived,
        CancellationToken ct)
    {
        await EnsurePermissionAsync(projectId, PermissionCatalog.WorkItemView, ct);
        var safePage = Math.Max(page, 1);
        var safeSize = Math.Clamp(pageSize, 1, 100);
        var total = await recurrences.CountByFilterAsync(
            recurrence => recurrence.ProjectId == projectId && (includeArchived || !recurrence.Archived), ct);
        var result = await recurrences.ListByFilterAsync(
            recurrence => recurrence.ProjectId == projectId && (includeArchived || !recurrence.Archived),
            recurrence => recurrence.CreatedAt,
            orderDescending: true,
            page: safePage,
            pageSize: safeSize,
            cancellationToken: ct);
        var responses = new List<WorkItemRecurrenceResponse>(result.Count);
        foreach (var recurrence in result)
        {
            responses.Add(await ToResponseAsync(recurrence, ct));
        }
        return new WorkItemRecurrencePage(responses, safePage, safeSize, total);
    }
}
