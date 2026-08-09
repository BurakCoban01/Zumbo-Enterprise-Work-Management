using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.Modules.WorkItems;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems.Application.Features.Recurrences;

public sealed class ListWorkItemTemplatesHandler(WorkItemTemplateRecurrenceService service)
{
    private ListWorkItemTemplatesSlice? slice;

    public ListWorkItemTemplatesHandler(
        IDocumentRepository<WorkItemTemplateDocument> templates,
        IProjectPermissionChecker permissionChecker,
        ICurrentUser currentUser)
        : this(null!)
    {
        slice = new ListWorkItemTemplatesSlice(templates, permissionChecker, currentUser);
    }

    public Task<WorkItemTemplatePage> HandleAsync(
        ListWorkItemTemplatesQuery query,
        CancellationToken ct) =>
        slice?.HandleAsync(query, ct)
        ?? service.ListTemplatesAsync(
            query.ProjectId,
            query.Page,
            query.PageSize,
            query.IncludeArchived,
            ct);
}
