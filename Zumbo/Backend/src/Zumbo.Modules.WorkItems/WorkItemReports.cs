using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed partial class WorkItemService
{
    public async Task<ProjectSummaryResponse> ProjectSummaryAsync(string projectId, CancellationToken ct) =>
        (await ProjectSummarySnapshotAsync(projectId, ct)).Data;

    public async Task<WorkItemReportSnapshot<ProjectSummaryResponse>> ProjectSummarySnapshotAsync(
        string projectId,
        CancellationToken ct)
    {
        await EnsurePermissionAsync(projectId, "WorkItemView", ct);
        return await readModelCache.GetOrCreateSnapshotAsync(
            projectId,
            "project-summary",
            ReadModelTtl,
            async token =>
            {
                var now = clock.UtcNow;
                return new ProjectSummaryResponse(
                    checked((int)await workItems.CountByFilterAsync(
                        x => x.ProjectId == projectId && !x.Archived, token)),
                    checked((int)await workItems.CountByFilterAsync(
                        x => x.ProjectId == projectId && !x.Archived && x.CompletedAt != null, token)),
                    checked((int)await workItems.CountByFilterAsync(
                        x => x.ProjectId == projectId && !x.Archived && x.CompletedAt == null
                            && (x.Status == "In Progress" || x.Status == "Code Review" || x.Status == "Test"),
                        token)),
                    checked((int)await workItems.CountByFilterAsync(
                        x => x.ProjectId == projectId && !x.Archived && x.DueDate < now && x.CompletedAt == null,
                        token)));
            },
            ct);
    }

    public async Task<IReadOnlyList<StatusDistributionResponse>> StatusDistributionAsync(
        string projectId,
        CancellationToken ct) =>
        (await StatusDistributionSnapshotAsync(projectId, ct)).Data;

    public async Task<WorkItemReportSnapshot<IReadOnlyList<StatusDistributionResponse>>> StatusDistributionSnapshotAsync(
        string projectId,
        CancellationToken ct)
    {
        await EnsurePermissionAsync(projectId, "WorkItemView", ct);
        return await readModelCache.GetOrCreateSnapshotAsync<IReadOnlyList<StatusDistributionResponse>>(
            projectId,
            "status-distribution",
            ReadModelTtl,
            async token => (await LoadReportItemsAsync(
                    item => item.ProjectId == projectId && !item.Archived,
                    token))
                .GroupBy(x => x.Status)
                .OrderBy(x => x.Key)
                .Select(x => new StatusDistributionResponse(x.Key, x.Count()))
                .ToList(),
            ct);
    }

    public async Task<IReadOnlyList<UserWorkloadResponse>> UserWorkloadAsync(
        string projectId,
        CancellationToken ct) =>
        (await UserWorkloadSnapshotAsync(projectId, ct)).Data;

    public async Task<WorkItemReportSnapshot<IReadOnlyList<UserWorkloadResponse>>> UserWorkloadSnapshotAsync(
        string projectId,
        CancellationToken ct)
    {
        await EnsurePermissionAsync(projectId, "WorkItemView", ct);
        return await readModelCache.GetOrCreateSnapshotAsync<IReadOnlyList<UserWorkloadResponse>>(
            projectId,
            "user-workload",
            ReadModelTtl,
            async token =>
            {
                var now = clock.UtcNow;
                var result = await LoadReportItemsAsync(
                    item => item.ProjectId == projectId && !item.Archived,
                    token);
                var activities = await ReadReportActivitiesAsync(projectId, token);
                return result
                    .Where(x => !string.IsNullOrWhiteSpace(x.AssigneeUserId))
                    .GroupBy(x => x.AssigneeUserId!)
                    .OrderBy(x => x.Key)
                    .Select(x => new UserWorkloadResponse(
                        x.Key,
                        x.Count(item => item.CompletedAt is null),
                        x.Count(item => item.DueDate < now && item.CompletedAt is null),
                        x.Sum(item => LoggedHours(item, activities))))
                    .ToList();
            },
            ct);
    }

    public async Task<IReadOnlyList<DueDateRiskResponse>> DueDateRisksAsync(
        string projectId,
        int days,
        CancellationToken ct) =>
        (await DueDateRisksSnapshotAsync(projectId, days, ct)).Data;

    public async Task<WorkItemReportSnapshot<IReadOnlyList<DueDateRiskResponse>>> DueDateRisksSnapshotAsync(
        string projectId,
        int days,
        CancellationToken ct)
    {
        await EnsurePermissionAsync(projectId, "WorkItemView", ct);
        var normalizedDays = Math.Clamp(days, 1, 90);
        return await readModelCache.GetOrCreateSnapshotAsync<IReadOnlyList<DueDateRiskResponse>>(
            projectId,
            $"due-date-risks:{normalizedDays}",
            ReadModelTtl,
            async token =>
            {
                var until = clock.UtcNow.AddDays(normalizedDays);
                var result = await LoadReportItemsAsync(
                    x => x.ProjectId == projectId && !x.Archived && x.CompletedAt == null
                        && x.DueDate != null && x.DueDate <= until,
                    token);
                return result
                    .OrderBy(x => x.DueDate)
                    .ThenBy(x => x.Id, StringComparer.Ordinal)
                    .Select(x => new DueDateRiskResponse(
                        x.Id, x.Title, x.AssigneeUserId, x.DueDate!.Value, x.Status))
                    .ToList();
            },
            ct);
    }

    public async Task<FlowTimeReportResponse> FlowTimeAsync(
        string projectId,
        DateOnly? from,
        DateOnly? to,
        CancellationToken ct) =>
        (await FlowTimeSnapshotAsync(projectId, from, to, ct)).Data;

    public async Task<WorkItemReportSnapshot<FlowTimeReportResponse>> FlowTimeSnapshotAsync(
        string projectId,
        DateOnly? from,
        DateOnly? to,
        CancellationToken ct)
    {
        await EnsurePermissionAsync(projectId, "WorkItemView", ct);
        var reportTo = to ?? DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);
        var reportFrom = from ?? reportTo.AddDays(-29);
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
            projectId,
            $"flow-time:{reportFrom:yyyyMMdd}:{reportTo:yyyyMMdd}",
            ReadModelTtl,
            async token =>
            {
                var completed = await LoadReportItemsAsync(
                    x => x.ProjectId == projectId && !x.Archived && x.CompletedAt != null
                        && x.CompletedAt >= fromInstant && x.CompletedAt <= toInstant,
                    token);
                var activities = await ReadReportActivitiesAsync(projectId, token);
                var leadTimes = completed
                    .Select(x => Math.Max(0, (x.CompletedAt!.Value - x.CreatedAt).TotalHours))
                    .ToList();
                var cycleTimes = completed
                    .Select(item => TryCalculateCycleTimeHours(item, Timeline(item, activities)))
                    .Where(x => x.HasValue)
                    .Select(x => x!.Value)
                    .ToList();
                return new FlowTimeReportResponse(
                    reportFrom, reportTo, completed.Count, cycleTimes.Count,
                    Average(leadTimes) ?? 0, Median(leadTimes) ?? 0,
                    Average(cycleTimes), Median(cycleTimes));
            },
            ct);
    }

    public async Task<TaskCompletionRateResponse> CompletionRateAsync(
        string projectId,
        DateOnly? from,
        DateOnly? to,
        CancellationToken ct) =>
        (await CompletionRateSnapshotAsync(projectId, from, to, ct)).Data;

    public async Task<WorkItemReportSnapshot<TaskCompletionRateResponse>> CompletionRateSnapshotAsync(
        string projectId,
        DateOnly? from,
        DateOnly? to,
        CancellationToken ct)
    {
        await EnsurePermissionAsync(projectId, "WorkItemView", ct);
        var range = NormalizeReportRange(from, to);
        return await readModelCache.GetOrCreateSnapshotAsync(
            projectId,
            $"completion-rate:{range.From:yyyyMMdd}:{range.To:yyyyMMdd}",
            ReadModelTtl,
            async token =>
            {
                var items = await LoadReportItemsAsync(
                    x => x.ProjectId == projectId && !x.Archived
                        && x.CreatedAt >= range.FromInstant && x.CreatedAt <= range.ToInstant,
                    token);
                var completed = items.Count(x => x.CompletedAt is not null && x.CompletedAt <= range.ToInstant);
                return new TaskCompletionRateResponse(
                    range.From, range.To, items.Count, completed,
                    items.Count == 0 ? 0 : Math.Round(completed * 100d / items.Count, 2));
            },
            ct);
    }

    public async Task<IReadOnlyList<TeamPerformanceResponse>> TeamPerformanceAsync(
        string projectId,
        DateOnly? from,
        DateOnly? to,
        CancellationToken ct) =>
        (await TeamPerformanceSnapshotAsync(projectId, from, to, ct)).Data;

    public async Task<WorkItemReportSnapshot<IReadOnlyList<TeamPerformanceResponse>>> TeamPerformanceSnapshotAsync(
        string projectId,
        DateOnly? from,
        DateOnly? to,
        CancellationToken ct)
    {
        await EnsurePermissionAsync(projectId, "WorkItemView", ct);
        var range = NormalizeReportRange(from, to);
        return await readModelCache.GetOrCreateSnapshotAsync<IReadOnlyList<TeamPerformanceResponse>>(
            projectId,
            $"team-performance:{range.From:yyyyMMdd}:{range.To:yyyyMMdd}",
            ReadModelTtl,
            async token =>
            {
                var teams = await teamPolicy.ListProjectTeamsAsync(projectId, token);
                var items = await LoadReportItemsAsync(
                    x => x.ProjectId == projectId && !x.Archived && x.TeamId != null
                        && x.CreatedAt >= range.FromInstant && x.CreatedAt <= range.ToInstant,
                    token);
                var activities = await ReadReportActivitiesAsync(projectId, token);
                return teams.OrderBy(x => x.Name).Select(team =>
                {
                    var assigned = items.Where(x => x.TeamId == team.Id).ToList();
                    var completed = assigned
                        .Where(x => x.CompletedAt is not null && x.CompletedAt <= range.ToInstant)
                        .ToList();
                    var leadTimes = completed
                        .Select(x => Math.Max(0, (x.CompletedAt!.Value - x.CreatedAt).TotalHours))
                        .ToList();
                    return new TeamPerformanceResponse(
                        team.Id, team.Name, assigned.Count, completed.Count,
                        assigned.Count == 0 ? 0 : Math.Round(completed.Count * 100d / assigned.Count, 2),
                        Average(leadTimes), assigned.Sum(item => LoggedHours(item, activities)));
                }).ToList();
            },
            ct);
    }
}
