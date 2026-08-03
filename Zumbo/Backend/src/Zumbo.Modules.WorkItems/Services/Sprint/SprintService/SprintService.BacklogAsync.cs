using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.Modules.WorkItems.Domain;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed partial class SprintService{

    public async Task<SprintBacklogPageResponse> BacklogAsync(
        string projectId,
        string? after,
        int pageSize,
        CancellationToken ct)
    {
        await EnsurePermissionAsync(projectId, PermissionCatalog.WorkItemView, ct);
        var page = await workItems.ListByCursorAsync(
            item => item.ProjectId == projectId && !item.Archived && item.SprintId == null,
            NormalizeOptional(after),
            Math.Clamp(pageSize, 1, 100),
            ct);
        return new SprintBacklogPageResponse(
            page.Items.Select(item => new SprintBacklogItemResponse(
                item.Id,
                item.Title,
                item.Type,
                item.Priority,
                item.EstimatePoints,
                item.Rank,
                item.Version)).ToList(),
            page.NextCursor);
    }
}
