using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

internal sealed class DueDateRisksPipeline(
    IDocumentRepository<WorkItemDocument> workItems,
    IClock clock,
    ICurrentUser currentUser,
    IProjectPermissionChecker permissionChecker,
    IWorkItemReadModelCache readModelCache,
    IOptions<WorkItemReadModelCacheOptions> readModelCacheOptions)
{
    internal async Task<WorkItemReportSnapshot<IReadOnlyList<DueDateRiskResponse>>> GetAsync(
        DueDateRisksQuery query,
        CancellationToken ct)
    {
        await EnsurePermissionAsync(query.ProjectId, PermissionCatalog.WorkItemView, ct);
        var normalizedDays = Math.Clamp(query.Days, 1, 90);
        return await readModelCache.GetOrCreateSnapshotAsync<IReadOnlyList<DueDateRiskResponse>>(
            query.ProjectId,
            $"due-date-risks:{normalizedDays}",
            TimeSpan.FromSeconds(Math.Clamp(readModelCacheOptions.Value.TtlSeconds, 5, 300)),
            async token =>
            {
                var until = clock.UtcNow.AddDays(normalizedDays);
                var result = await LoadReportItemsAsync(query.ProjectId, until, token);
                return result
                    .OrderBy(item => item.DueDate)
                    .ThenBy(item => item.Id, StringComparer.Ordinal)
                    .Select(item => new DueDateRiskResponse(
                        item.Id,
                        item.Title,
                        item.AssigneeUserId,
                        item.DueDate!.Value,
                        item.Status))
                    .ToList();
            },
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
        DateTimeOffset until,
        CancellationToken ct)
    {
        const int pageSize = 200;
        var result = new List<WorkItemDocument>();
        string? cursor = null;
        do
        {
            var page = await workItems.ListByCursorAsync(
                item => item.ProjectId == projectId
                    && !item.Archived
                    && item.CompletedAt == null
                    && item.DueDate != null
                    && item.DueDate <= until,
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
