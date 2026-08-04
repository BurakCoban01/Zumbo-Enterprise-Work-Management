using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.Modules.WorkItems.Domain;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed partial class SprintService{

    public async Task<SprintCursorPageResponse> ListAsync(
        string projectId,
        string? after,
        int pageSize,
        CancellationToken ct)
    {
        await EnsurePermissionAsync(projectId, PermissionCatalog.WorkItemView, ct);
        var page = await sprints.ListByCursorAsync(
            sprint => sprint.ProjectId == projectId,
            NormalizeOptional(after),
            Math.Clamp(pageSize, 1, 100),
            ct);
        return new SprintCursorPageResponse(page.Items.Select(ToResponse).ToList(), page.NextCursor);
    }
}
