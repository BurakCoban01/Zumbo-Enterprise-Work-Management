using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.Modules.WorkItems;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems.Application.Features.Recurrences;

internal sealed class ListRecurrenceOccurrencesSlice(
    IDocumentRepository<WorkItemRecurrenceDocument> recurrences,
    IDocumentRepository<WorkItemRecurrenceOccurrenceDocument> occurrences,
    IProjectPermissionChecker permissionChecker,
    ICurrentUser currentUser)
{
    private readonly RecurrenceReadAccess access = new(recurrences, permissionChecker, currentUser);

    internal async Task<WorkItemRecurrenceOccurrencePage> HandleAsync(
        ListRecurrenceOccurrencesQuery query,
        CancellationToken ct)
    {
        var recurrence = await access.GetRecurrenceAsync(
            query.RecurrenceId,
            includeArchived: true,
            ct);
        await access.AuthorizeProjectAsync(recurrence.ProjectId, ct);
        var safePage = Math.Max(query.Page, 1);
        var safeSize = Math.Clamp(query.PageSize, 1, 100);
        var total = await occurrences.CountByFilterAsync(
            occurrence => occurrence.RecurrenceId == recurrence.Id,
            ct);
        var result = await occurrences.ListByFilterAsync(
            occurrence => occurrence.RecurrenceId == recurrence.Id,
            occurrence => occurrence.ScheduledForUtc,
            orderDescending: true,
            page: safePage,
            pageSize: safeSize,
            cancellationToken: ct);
        return new WorkItemRecurrenceOccurrencePage(
            result.Select(RecurrenceResponseMapper.ToOccurrenceResponse).ToList(),
            safePage,
            safeSize,
            total);
    }
}
