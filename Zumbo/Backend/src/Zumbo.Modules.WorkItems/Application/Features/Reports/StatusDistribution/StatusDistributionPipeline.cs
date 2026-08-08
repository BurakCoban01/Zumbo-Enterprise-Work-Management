using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

internal sealed class StatusDistributionPipeline(
    IDocumentRepository<WorkItemDocument> workItems,
    ICurrentUser currentUser,
    IProjectPermissionChecker permissionChecker,
    IWorkItemReadModelCache readModelCache,
    IOptions<WorkItemReadModelCacheOptions> readModelCacheOptions)
{
    internal async Task<WorkItemReportSnapshot<IReadOnlyList<StatusDistributionResponse>>> GetAsync(
        StatusDistributionQuery query,
        CancellationToken ct)
    {
        await EnsurePermissionAsync(query.ProjectId, PermissionCatalog.WorkItemView, ct);
        return await readModelCache.GetOrCreateSnapshotAsync<IReadOnlyList<StatusDistributionResponse>>(
            query.ProjectId,
            "status-distribution",
            TimeSpan.FromSeconds(Math.Clamp(readModelCacheOptions.Value.TtlSeconds, 5, 300)),
            async token => (await LoadReportItemsAsync(query.ProjectId, token))
                .GroupBy(item => item.Status)
                .OrderBy(group => group.Key)
                .Select(group => new StatusDistributionResponse(group.Key, group.Count()))
                .ToList(),
            ct);
    }

    private async Task EnsurePermissionAsync(string projectId, string permission, CancellationToken ct)
    {
        var userId = currentUser.UserId;
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new UnauthorizedException("Authenticated user is required.");
        }

        await permissionChecker.EnsureCanAsync(userId, projectId, permission, ct);
    }

    private async Task<IReadOnlyList<WorkItemDocument>> LoadReportItemsAsync(
        string projectId,
        CancellationToken ct)
    {
        const int pageSize = 200;
        var result = new List<WorkItemDocument>();
        string? cursor = null;
        do
        {
            var page = await workItems.ListByCursorAsync(
                item => item.ProjectId == projectId && !item.Archived,
                cursor,
                pageSize,
                ct);
            result.AddRange(page.Items);
            cursor = page.NextCursor;
        }
        while (cursor is not null);

        return result;
    }
}
