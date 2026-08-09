using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.Modules.WorkItems;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems.Application.Features.Recurrences;

public sealed class ListWorkItemRecurrencesHandler(WorkItemTemplateRecurrenceService service)
{
    private ListWorkItemRecurrencesSlice? slice;

    public ListWorkItemRecurrencesHandler(
        IDocumentRepository<WorkItemRecurrenceDocument> recurrences,
        IDocumentRepository<WorkItemRecurrenceOccurrenceDocument> occurrences,
        IProjectPermissionChecker permissionChecker,
        ICurrentUser currentUser)
        : this(null!)
    {
        slice = new ListWorkItemRecurrencesSlice(
            recurrences,
            occurrences,
            permissionChecker,
            currentUser);
    }

    public Task<WorkItemRecurrencePage> HandleAsync(
        ListWorkItemRecurrencesQuery query,
        CancellationToken ct) =>
        slice?.HandleAsync(query, ct)
        ?? service.ListRecurrencesAsync(
            query.ProjectId,
            query.Page,
            query.PageSize,
            query.IncludeArchived,
            ct);
}
