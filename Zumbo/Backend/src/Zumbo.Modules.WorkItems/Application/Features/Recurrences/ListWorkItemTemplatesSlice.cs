using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.Modules.WorkItems;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems.Application.Features.Recurrences;

internal sealed class ListWorkItemTemplatesSlice(
    IDocumentRepository<WorkItemTemplateDocument> templates,
    IProjectPermissionChecker permissionChecker,
    ICurrentUser currentUser)
{
    internal async Task<WorkItemTemplatePage> HandleAsync(
        ListWorkItemTemplatesQuery query,
        CancellationToken ct)
    {
        var userId = currentUser.UserId
            ?? throw new UnauthorizedException("Authenticated user is required.");
        _ = await permissionChecker.EnsureCanAsync(
            userId,
            query.ProjectId,
            PermissionCatalog.WorkItemView,
            ct);
        var safePage = Math.Max(query.Page, 1);
        var safeSize = Math.Clamp(query.PageSize, 1, 100);
        var total = await templates.CountByFilterAsync(
            template => template.ProjectId == query.ProjectId
                && (query.IncludeArchived || !template.Archived),
            ct);
        var result = await templates.ListByFilterAsync(
            template => template.ProjectId == query.ProjectId
                && (query.IncludeArchived || !template.Archived),
            template => template.Name,
            page: safePage,
            pageSize: safeSize,
            cancellationToken: ct);
        return new WorkItemTemplatePage(
            result.Select(WorkItemTemplateResponseMapper.ToResponse).ToList(),
            safePage,
            safeSize,
            total);
    }
}
