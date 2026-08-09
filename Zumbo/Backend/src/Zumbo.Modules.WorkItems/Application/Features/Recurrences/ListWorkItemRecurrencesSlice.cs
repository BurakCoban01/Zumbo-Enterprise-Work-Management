using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.Modules.WorkItems;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems.Application.Features.Recurrences;

internal sealed class ListWorkItemRecurrencesSlice(
    IDocumentRepository<WorkItemRecurrenceDocument> recurrences,
    IDocumentRepository<WorkItemRecurrenceOccurrenceDocument> occurrences,
    IProjectPermissionChecker permissionChecker,
    ICurrentUser currentUser)
{
    private readonly RecurrenceReadAccess access = new(recurrences, permissionChecker, currentUser);
    private readonly RecurrenceResponseMapper mapper = new(occurrences);

    internal async Task<WorkItemRecurrencePage> HandleAsync(
        ListWorkItemRecurrencesQuery query,
        CancellationToken ct)
    {
        await access.AuthorizeProjectAsync(query.ProjectId, ct);
        var safePage = Math.Max(query.Page, 1);
        var safeSize = Math.Clamp(query.PageSize, 1, 100);
        var total = await recurrences.CountByFilterAsync(
            recurrence => recurrence.ProjectId == query.ProjectId
                && (query.IncludeArchived || !recurrence.Archived),
            ct);
        var result = await recurrences.ListByFilterAsync(
            recurrence => recurrence.ProjectId == query.ProjectId
                && (query.IncludeArchived || !recurrence.Archived),
            recurrence => recurrence.CreatedAt,
            orderDescending: true,
            page: safePage,
            pageSize: safeSize,
            cancellationToken: ct);
        var responses = new List<WorkItemRecurrenceResponse>(result.Count);
        foreach (var recurrence in result)
            responses.Add(await mapper.ToResponseAsync(recurrence, ct));
        return new WorkItemRecurrencePage(responses, safePage, safeSize, total);
    }
}
