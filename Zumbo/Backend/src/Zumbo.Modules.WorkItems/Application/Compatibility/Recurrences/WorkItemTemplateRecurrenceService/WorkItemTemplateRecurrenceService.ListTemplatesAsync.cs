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

    public async Task<WorkItemTemplatePage> ListTemplatesAsync(
        string projectId,
        int page,
        int pageSize,
        bool includeArchived,
        CancellationToken ct)
    {
        await EnsurePermissionAsync(projectId, PermissionCatalog.WorkItemView, ct);
        var safePage = Math.Max(page, 1);
        var safeSize = Math.Clamp(pageSize, 1, 100);
        var total = await templates.CountByFilterAsync(
            template => template.ProjectId == projectId && (includeArchived || !template.Archived), ct);
        var result = await templates.ListByFilterAsync(
            template => template.ProjectId == projectId && (includeArchived || !template.Archived),
            template => template.Name,
            page: safePage,
            pageSize: safeSize,
            cancellationToken: ct);
        return new WorkItemTemplatePage(result.Select(ToResponse).ToList(), safePage, safeSize, total);
    }
}
