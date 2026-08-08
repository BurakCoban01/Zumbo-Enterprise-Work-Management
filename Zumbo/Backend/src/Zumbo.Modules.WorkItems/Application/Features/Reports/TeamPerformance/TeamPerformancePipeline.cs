using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

internal sealed class TeamPerformancePipeline(
    IDocumentRepository<WorkItemDocument> workItems,
    IClock clock,
    ICurrentUser currentUser,
    IProjectPermissionChecker permissionChecker,
    IWorkItemTeamPolicy teamPolicy,
    IWorkItemReadModelCache readModelCache,
    IOptions<WorkItemReadModelCacheOptions> readModelCacheOptions,
    IWorkItemActivityStore activityStore)
{
    internal async Task<WorkItemReportSnapshot<IReadOnlyList<TeamPerformanceResponse>>> GetAsync(
        TeamPerformanceQuery query,
        CancellationToken ct)
    {
        var authorization = await EnsurePermissionAsync(
            query.ProjectId,
            PermissionCatalog.WorkItemView,
            ct);
        var range = NormalizeReportRange(query.From, query.To);
        return await readModelCache.GetOrCreateSnapshotAsync<IReadOnlyList<TeamPerformanceResponse>>(
            query.ProjectId,
            $"team-performance:{range.From:yyyyMMdd}:{range.To:yyyyMMdd}",
            TimeSpan.FromSeconds(Math.Clamp(readModelCacheOptions.Value.TtlSeconds, 5, 300)),
            async token =>
            {
                var teams = await teamPolicy.ListProjectTeamsAsync(query.ProjectId, token);
                var items = await LoadReportItemsAsync(query.ProjectId, range, token);
                var activities = await activityStore.ReadReportDataAsync(
                    authorization.OrganizationId,
                    query.ProjectId,
                    token);
                return teams.OrderBy(team => team.Name).Select(team =>
                {
                    var assigned = items.Where(item => item.TeamId == team.Id).ToList();
                    var completed = assigned
                        .Where(item => item.CompletedAt is not null
                            && item.CompletedAt <= range.ToInstant)
                        .ToList();
                    var leadTimes = completed
                        .Select(item => Math.Max(
                            0,
                            (item.CompletedAt!.Value - item.CreatedAt).TotalHours))
                        .ToList();
                    return new TeamPerformanceResponse(
                        team.Id,
                        team.Name,
                        assigned.Count,
                        completed.Count,
                        assigned.Count == 0
                            ? 0
                            : Math.Round(completed.Count * 100d / assigned.Count, 2),
                        Average(leadTimes),
                        assigned.Sum(item => LoggedHours(item, activities)));
                }).ToList();
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

    private ReportRange NormalizeReportRange(DateOnly? from, DateOnly? to)
    {
        var reportTo = to ?? DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);
        var reportFrom = from ?? reportTo.AddDays(-29);
        if (reportTo < reportFrom)
        {
            throw new ValidationException("Report end date must be after start date.");
        }

        if (reportTo.DayNumber - reportFrom.DayNumber + 1 > 366)
        {
            throw new ValidationException("Report range cannot exceed 366 days.");
        }

        return new ReportRange(
            reportFrom,
            reportTo,
            new DateTimeOffset(reportFrom.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero),
            new DateTimeOffset(reportTo.ToDateTime(TimeOnly.MaxValue), TimeSpan.Zero));
    }

    private async Task<IReadOnlyList<WorkItemDocument>> LoadReportItemsAsync(
        string projectId,
        ReportRange range,
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
                    && item.TeamId != null
                    && item.CreatedAt >= range.FromInstant
                    && item.CreatedAt <= range.ToInstant,
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

    private static double? Average(IReadOnlyCollection<double> values) =>
        values.Count == 0 ? null : Math.Round(values.Average(), 2);

    private sealed record ReportRange(
        DateOnly From,
        DateOnly To,
        DateTimeOffset FromInstant,
        DateTimeOffset ToInstant);
}
