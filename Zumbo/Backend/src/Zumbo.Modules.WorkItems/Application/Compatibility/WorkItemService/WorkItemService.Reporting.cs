using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Linq.Expressions;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Runtime;
using Zumbo.BuildingBlocks.Application.Search;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed partial class WorkItemService
{
    private async Task<IReadOnlyList<WorkItemDocument>> LoadReportItemsAsync(
        Expression<Func<WorkItemDocument, bool>> filter,
        CancellationToken ct)
    {
        const int pageSize = 200;
        var result = new List<WorkItemDocument>();
        string? cursor = null;
        do
        {
            var page = await workItems.ListByCursorAsync(
                filter,
                cursor,
                pageSize,
                ct);
            result.AddRange(page.Items);
            cursor = page.NextCursor;
        }
        while (cursor is not null);

        return result;
    }

    private Task<WorkItemReportActivityData> ReadReportActivitiesAsync(
        string projectId,
        CancellationToken ct) =>
        activityStore.ReadReportDataAsync(CurrentOrganizationId(projectId), projectId, ct);

    private static decimal LoggedHours(WorkItemDocument item, WorkItemReportActivityData activities) =>
        item.ActivityStorageVersion >= 1
            ? activities.LoggedHoursByWorkItem.GetValueOrDefault(item.Id)
            : item.WorkLogs.Sum(log => log.Hours);

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
}
