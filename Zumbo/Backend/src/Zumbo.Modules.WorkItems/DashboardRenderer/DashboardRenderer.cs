using System.Globalization;
using System.Text.Json;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed class DashboardRenderer(
    DashboardService dashboards,
    WorkItemService reports,
    IClock clock)
{
    public async Task<DashboardRenderResponse> RenderAsync(
        string dashboardId,
        CancellationToken ct)
    {
        var dashboard = await dashboards.GetAsync(dashboardId, includeArchived: false, ct);
        var widgets = new List<DashboardRenderedWidgetResponse>(dashboard.Widgets.Count);
        foreach (var widget in dashboard.Widgets.OrderBy(item => item.Row).ThenBy(item => item.Column))
        {
            var sources = new List<DashboardWidgetSourceResponse>();
            try
            {
                var projectIds = widget.ProjectId is null
                    ? dashboard.ProjectIds
                    : [widget.ProjectId];
                foreach (var projectId in projectIds)
                {
                    sources.Add(await RenderSourceAsync(
                        projectId,
                        widget.Type,
                        widget.Filter ?? dashboard.Filter,
                        ct));
                }
                widgets.Add(new DashboardRenderedWidgetResponse(
                    widget.Id,
                    widget.Type,
                    widget.Title,
                    sources.Any(source => source.Stale) ? "Stale" : "Ready",
                    null,
                    sources));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (NotFoundException)
            {
                throw;
            }
            catch (ForbiddenException)
            {
                throw;
            }
            catch
            {
                widgets.Add(new DashboardRenderedWidgetResponse(
                    widget.Id,
                    widget.Type,
                    widget.Title,
                    "Degraded",
                    "DASHBOARD_WIDGET_SOURCE_UNAVAILABLE",
                    sources));
            }
        }

        var renderedSources = widgets.SelectMany(widget => widget.Sources).ToList();
        return new DashboardRenderResponse(
            dashboard,
            widgets,
            renderedSources.Count == 0 ? null : renderedSources.Min(source => source.GeneratedAt),
            renderedSources.Select(source => source.SourceVersion)
                .Distinct()
                .Order()
                .ToList(),
            renderedSources.Any(source => source.Stale),
            widgets.Any(widget => widget.Status == "Degraded"),
            clock.UtcNow);
    }

    private async Task<DashboardWidgetSourceResponse> RenderSourceAsync(
        string projectId,
        string type,
        DashboardFilterRequest filter,
        CancellationToken ct)
    {
        var to = DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);
        var from = to.AddDays(-filter.RangeDays + 1);
        return type switch
        {
            DashboardWidgetTypes.ProjectSummary => Source(
                projectId,
                await reports.ProjectSummarySnapshotAsync(projectId, ct),
                [Column("total", "Toplam"), Column("done", "Tamamlanan"),
                    Column("inProgress", "Devam eden"), Column("overdue", "Geciken")],
                value => [Row(
                    ("total", value.Total),
                    ("done", value.Done),
                    ("inProgress", value.InProgress),
                    ("overdue", value.Overdue))]),
            DashboardWidgetTypes.StatusDistribution => Source(
                projectId,
                await reports.StatusDistributionSnapshotAsync(projectId, ct),
                [Column("status", "Durum"), Column("count", "İş sayısı")],
                values => values
                    .Where(value => filter.Statuses is null || filter.Statuses.Count == 0
                        || filter.Statuses.Contains(value.Status, StringComparer.OrdinalIgnoreCase))
                    .Select(value => Row(("status", value.Status), ("count", value.Count)))
                    .ToList()),
            DashboardWidgetTypes.UserWorkload => Source(
                projectId,
                await reports.UserWorkloadSnapshotAsync(projectId, ct),
                [Column("userId", "Kullanıcı"), Column("openItems", "Açık iş"),
                    Column("overdueItems", "Geciken"), Column("loggedHours", "Kayıtlı saat")],
                values => values
                    .Where(value => filter.AssigneeUserId is null
                        || value.UserId == filter.AssigneeUserId)
                    .Select(value => Row(
                        ("userId", value.UserId),
                        ("openItems", value.OpenItems),
                        ("overdueItems", value.OverdueItems),
                        ("loggedHours", value.LoggedHours)))
                    .ToList()),
            DashboardWidgetTypes.DueDateRisks => Source(
                projectId,
                await reports.DueDateRisksSnapshotAsync(projectId, filter.DueRiskDays, ct),
                [Column("title", "İş"), Column("assigneeUserId", "Atanan"),
                    Column("dueDate", "Son tarih"), Column("status", "Durum")],
                values => values
                    .Where(value => filter.AssigneeUserId is null
                        || value.AssigneeUserId == filter.AssigneeUserId)
                    .Where(value => filter.Statuses is null || filter.Statuses.Count == 0
                        || filter.Statuses.Contains(value.Status, StringComparer.OrdinalIgnoreCase))
                    .Select(value => Row(
                        ("title", value.Title),
                        ("assigneeUserId", value.AssigneeUserId),
                        ("dueDate", value.DueDate),
                        ("status", value.Status)))
                    .ToList()),
            DashboardWidgetTypes.FlowTime => Source(
                projectId,
                await reports.FlowTimeSnapshotAsync(projectId, from, to, ct),
                [Column("completedItems", "Tamamlanan"), Column("cycleTimeSampleSize", "Örnek"),
                    Column("medianLeadTimeHours", "Medyan lead time"),
                    Column("medianCycleTimeHours", "Medyan cycle time")],
                value => [Row(
                    ("completedItems", value.CompletedItems),
                    ("cycleTimeSampleSize", value.CycleTimeSampleSize),
                    ("medianLeadTimeHours", value.MedianLeadTimeHours),
                    ("medianCycleTimeHours", value.MedianCycleTimeHours))]),
            DashboardWidgetTypes.CompletionRate => Source(
                projectId,
                await reports.CompletionRateSnapshotAsync(projectId, from, to, ct),
                [Column("createdItems", "Oluşturulan"), Column("completedItems", "Tamamlanan"),
                    Column("completionRatePercent", "Tamamlama oranı")],
                value => [Row(
                    ("createdItems", value.CreatedItems),
                    ("completedItems", value.CompletedItems),
                    ("completionRatePercent", value.CompletionRatePercent))]),
            DashboardWidgetTypes.TeamPerformance => Source(
                projectId,
                await reports.TeamPerformanceSnapshotAsync(projectId, from, to, ct),
                [Column("teamName", "Ekip"), Column("assignedItems", "Atanan"),
                    Column("completedItems", "Tamamlanan"), Column("completionRatePercent", "Tamamlama oranı"),
                    Column("averageLeadTimeHours", "Ortalama lead time"), Column("loggedHours", "Kayıtlı saat")],
                values => values
                    .Where(value => filter.TeamId is null || value.TeamId == filter.TeamId)
                    .Select(value => Row(
                        ("teamName", value.TeamName),
                        ("assignedItems", value.AssignedItems),
                        ("completedItems", value.CompletedItems),
                        ("completionRatePercent", value.CompletionRatePercent),
                        ("averageLeadTimeHours", value.AverageLeadTimeHours),
                        ("loggedHours", value.LoggedHours)))
                    .ToList()),
            _ => throw new ValidationException($"Dashboard widget type '{type}' is not supported.")
        };
    }

    private static DashboardWidgetSourceResponse Source<T>(
        string projectId,
        WorkItemReportSnapshot<T> snapshot,
        IReadOnlyCollection<DashboardTableColumn> columns,
        Func<T, IReadOnlyCollection<IReadOnlyDictionary<string, string?>>> rows) =>
        new(
            projectId,
            JsonSerializer.SerializeToElement(snapshot.Data),
            columns,
            rows(snapshot.Data),
            snapshot.GeneratedAt,
            snapshot.SourceVersion,
            snapshot.Stale);

    private static DashboardTableColumn Column(string key, string label) => new(key, label);

    private static IReadOnlyDictionary<string, string?> Row(
        params (string Key, object? Value)[] values) =>
        values.ToDictionary(
            value => value.Key,
            value => Format(value.Value),
            StringComparer.Ordinal);

    private static string? Format(object? value) => value switch
    {
        null => null,
        DateTimeOffset dateTime => dateTime.ToString("O", CultureInfo.InvariantCulture),
        DateOnly date => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString()
    };
}
