using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

internal sealed class CompletionRatePipeline(
    IDocumentRepository<WorkItemDocument> workItems,
    IClock clock,
    ICurrentUser currentUser,
    IProjectPermissionChecker permissionChecker,
    IWorkItemReadModelCache readModelCache,
    IOptions<WorkItemReadModelCacheOptions> readModelCacheOptions)
{
    internal async Task<WorkItemReportSnapshot<TaskCompletionRateResponse>> GetAsync(
        CompletionRateQuery query,
        CancellationToken ct)
    {
        await EnsurePermissionAsync(query.ProjectId, PermissionCatalog.WorkItemView, ct);
        var range = NormalizeReportRange(query.From, query.To);
        return await readModelCache.GetOrCreateSnapshotAsync(
            query.ProjectId,
            $"completion-rate:{range.From:yyyyMMdd}:{range.To:yyyyMMdd}",
            TimeSpan.FromSeconds(Math.Clamp(readModelCacheOptions.Value.TtlSeconds, 5, 300)),
            async token =>
            {
                var items = await LoadReportItemsAsync(query.ProjectId, range, token);
                var completed = items.Count(item =>
                    item.CompletedAt is not null && item.CompletedAt <= range.ToInstant);
                return new TaskCompletionRateResponse(
                    range.From,
                    range.To,
                    items.Count,
                    completed,
                    items.Count == 0 ? 0 : Math.Round(completed * 100d / items.Count, 2));
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

    private sealed record ReportRange(
        DateOnly From,
        DateOnly To,
        DateTimeOffset FromInstant,
        DateTimeOffset ToInstant);
}
