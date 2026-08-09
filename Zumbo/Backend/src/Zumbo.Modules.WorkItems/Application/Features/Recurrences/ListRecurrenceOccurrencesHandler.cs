using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.Modules.WorkItems;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems.Application.Features.Recurrences;

public sealed class ListRecurrenceOccurrencesHandler(WorkItemTemplateRecurrenceService service)
{
    private ListRecurrenceOccurrencesSlice? slice;

    public ListRecurrenceOccurrencesHandler(
        IDocumentRepository<WorkItemRecurrenceDocument> recurrences,
        IDocumentRepository<WorkItemRecurrenceOccurrenceDocument> occurrences,
        IProjectPermissionChecker permissionChecker,
        ICurrentUser currentUser)
        : this(null!)
    {
        slice = new ListRecurrenceOccurrencesSlice(
            recurrences,
            occurrences,
            permissionChecker,
            currentUser);
    }

    public Task<WorkItemRecurrenceOccurrencePage> HandleAsync(
        ListRecurrenceOccurrencesQuery query,
        CancellationToken ct) =>
        slice?.HandleAsync(query, ct)
        ?? service.ListOccurrencesAsync(query.RecurrenceId, query.Page, query.PageSize, ct);
}
