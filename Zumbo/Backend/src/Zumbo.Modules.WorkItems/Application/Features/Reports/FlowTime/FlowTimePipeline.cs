using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

internal sealed class FlowTimePipeline(
    IDocumentRepository<WorkItemDocument> workItems,
    IClock clock,
    ICurrentUser currentUser,
    IProjectPermissionChecker permissionChecker,
    IWorkItemReadModelCache readModelCache,
    IOptions<WorkItemReadModelCacheOptions> readModelCacheOptions,
    IWorkItemActivityStore activityStore)
{
    internal async Task<WorkItemReportSnapshot<FlowTimeReportResponse>> GetAsync(
        FlowTimeQuery query,
        CancellationToken ct)
    {
        var authorization = await EnsurePermissionAsync(
            query.ProjectId,
            PermissionCatalog.WorkItemView,
            ct);
        var reportTo = query.To ?? DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);
        var reportFrom = query.From ?? reportTo.AddDays(-29);
        if (reportTo < reportFrom)
        {
            throw new ValidationException("Report end date must be after start date.");
        }

        if (reportTo.DayNumber - reportFrom.DayNumber + 1 > 366)
        {
            throw new ValidationException("Flow time report range cannot exceed 366 days.");
        }

        var fromInstant = new DateTimeOffset(reportFrom.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var toInstant = new DateTimeOffset(reportTo.ToDateTime(TimeOnly.MaxValue), TimeSpan.Zero);
        return await readModelCache.GetOrCreateSnapshotAsync(
            query.ProjectId,
            $"flow-time:{reportFrom:yyyyMMdd}:{reportTo:yyyyMMdd}",
            TimeSpan.FromSeconds(Math.Clamp(readModelCacheOptions.Value.TtlSeconds, 5, 300)),
            async token =>
            {
                var completed = await LoadCompletedItemsAsync(
                    query.ProjectId,
                    fromInstant,
                    toInstant,
                    token);
                var activities = await activityStore.ReadReportDataAsync(
                    authorization.OrganizationId,
                    query.ProjectId,
                    token);
                var leadTimes = completed
                    .Select(item => Math.Max(
                        0,
                        (item.CompletedAt!.Value - item.CreatedAt).TotalHours))
                    .ToList();
                var cycleTimes = completed
                    .Select(item => TryCalculateCycleTimeHours(item, Timeline(item, activities)))
                    .Where(value => value.HasValue)
                    .Select(value => value!.Value)
                    .ToList();
                return new FlowTimeReportResponse(
                    reportFrom,
                    reportTo,
                    completed.Count,
                    cycleTimes.Count,
                    Average(leadTimes) ?? 0,
                    Median(leadTimes) ?? 0,
                    Average(cycleTimes),
                    Median(cycleTimes));
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

    private async Task<IReadOnlyList<WorkItemDocument>> LoadCompletedItemsAsync(
        string projectId,
        DateTimeOffset fromInstant,
        DateTimeOffset toInstant,
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
                    && item.CompletedAt != null
                    && item.CompletedAt >= fromInstant
                    && item.CompletedAt <= toInstant,
                cursor,
                pageSize,
                ct);
            result.AddRange(page.Items);
            cursor = page.NextCursor;
        }
        while (cursor is not null);

        return result;
    }

    private static IReadOnlyList<WorkItemStatusHistoryResponse> Timeline(
        WorkItemDocument item,
        WorkItemReportActivityData activities) =>
        item.ActivityStorageVersion >= 1
            ? activities.TimelineByWorkItem.GetValueOrDefault(item.Id) ?? []
            : item.StatusHistory
                .Select(entry => new WorkItemStatusHistoryResponse(
                    entry.FromStatus,
                    entry.ToStatus,
                    entry.ChangedByUserId,
                    entry.ChangedAt))
                .ToList();

    private static double? TryCalculateCycleTimeHours(
        WorkItemDocument item,
        IReadOnlyCollection<WorkItemStatusHistoryResponse> statusHistory)
    {
        if (item.CompletedAt is null)
        {
            return null;
        }

        var history = statusHistory.OrderBy(entry => entry.ChangedAt).ToList();
        var previousCompletion = history
            .Where(entry => entry.ChangedAt < item.CompletedAt && IsCompletedStatus(entry.ToStatus))
            .Select(entry => (DateTimeOffset?)entry.ChangedAt)
            .LastOrDefault();
        var startedAt = history
            .Where(entry => entry.ChangedAt <= item.CompletedAt
                && (!previousCompletion.HasValue || entry.ChangedAt > previousCompletion.Value)
                && IsActiveStatus(entry.ToStatus))
            .Select(entry => (DateTimeOffset?)entry.ChangedAt)
            .FirstOrDefault();

        return startedAt.HasValue
            ? Math.Max(0, (item.CompletedAt.Value - startedAt.Value).TotalHours)
            : null;
    }

    private static bool IsCompletedStatus(string status) =>
        status.Equals("Done", StringComparison.OrdinalIgnoreCase)
        || status.Equals("Closed", StringComparison.OrdinalIgnoreCase);

    private static bool IsActiveStatus(string status) =>
        !status.Equals("Backlog", StringComparison.OrdinalIgnoreCase)
        && !status.Equals("To Do", StringComparison.OrdinalIgnoreCase)
        && !status.Equals("Open", StringComparison.OrdinalIgnoreCase)
        && !IsCompletedStatus(status);

    private static double? Average(IReadOnlyCollection<double> values) =>
        values.Count == 0 ? null : Math.Round(values.Average(), 2);

    private static double? Median(IReadOnlyCollection<double> values)
    {
        if (values.Count == 0)
        {
            return null;
        }

        var ordered = values.OrderBy(value => value).ToArray();
        var middle = ordered.Length / 2;
        var value = ordered.Length % 2 == 0
            ? (ordered[middle - 1] + ordered[middle]) / 2
            : ordered[middle];
        return Math.Round(value, 2);
    }
}
