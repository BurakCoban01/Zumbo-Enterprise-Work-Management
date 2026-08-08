using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

internal sealed class UserWorkloadPipeline(
    IDocumentRepository<WorkItemDocument> workItems,
    IClock clock,
    ICurrentUser currentUser,
    IProjectPermissionChecker permissionChecker,
    IWorkItemReadModelCache readModelCache,
    IOptions<WorkItemReadModelCacheOptions> readModelCacheOptions,
    IWorkItemActivityStore activityStore)
{
    internal async Task<WorkItemReportSnapshot<IReadOnlyList<UserWorkloadResponse>>> GetAsync(
        UserWorkloadQuery query,
        CancellationToken ct)
    {
        var authorization = await EnsurePermissionAsync(
            query.ProjectId,
            PermissionCatalog.WorkItemView,
            ct);
        return await readModelCache.GetOrCreateSnapshotAsync<IReadOnlyList<UserWorkloadResponse>>(
            query.ProjectId,
            "user-workload",
            TimeSpan.FromSeconds(Math.Clamp(readModelCacheOptions.Value.TtlSeconds, 5, 300)),
            async token =>
            {
                var now = clock.UtcNow;
                var result = await LoadReportItemsAsync(query.ProjectId, token);
                var activities = await activityStore.ReadReportDataAsync(
                    authorization.OrganizationId,
                    query.ProjectId,
                    token);
                return result
                    .Where(item => !string.IsNullOrWhiteSpace(item.AssigneeUserId))
                    .GroupBy(item => item.AssigneeUserId!)
                    .OrderBy(group => group.Key)
                    .Select(group => new UserWorkloadResponse(
                        group.Key,
                        group.Count(item => item.CompletedAt is null),
                        group.Count(item => item.DueDate < now && item.CompletedAt is null),
                        group.Sum(item => LoggedHours(item, activities))))
                    .ToList();
            },
            ct);
    }

    private async Task<ProjectResourceAuthorization> EnsurePermissionAsync(
        string projectId,
        string permission,
        CancellationToken ct)
    {
        var userId = currentUser.UserId;
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new UnauthorizedException("Authenticated user is required.");
        }

        return await permissionChecker.EnsureCanAsync(userId, projectId, permission, ct);
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

    private static decimal LoggedHours(
        WorkItemDocument item,
        WorkItemReportActivityData activities) =>
        item.ActivityStorageVersion >= 1
            ? activities.LoggedHoursByWorkItem.GetValueOrDefault(item.Id)
            : item.WorkLogs.Sum(log => log.Hours);
}
