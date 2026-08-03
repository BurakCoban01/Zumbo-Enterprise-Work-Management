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
    public async Task<int> SendDueDateRemindersAsync(int horizonHours, CancellationToken ct)
    {
        await using var dispatcherLock = await AcquireRequiredLockAsync("due-date-reminder-dispatcher", ct);
        var now = clock.UtcNow;
        var until = now.AddHours(Math.Clamp(horizonHours, 1, 168));
        var candidates = await workItems.ListByFilterAsync(
            x => !x.Archived
                && x.CompletedAt == null
                && x.AssigneeUserId != null
                && x.DueDate != null
                && x.DueDate > now
                && x.DueDate <= until
                && x.DueReminderSentAt == null,
            x => x.DueDate!,
            pageSize: 500,
            cancellationToken: ct);
        var sent = 0;
        foreach (var candidate in candidates)
        {
            await using var workItemLock = await AcquireRequiredLockAsync("work-item:" + candidate.Id, ct);
            var workItem = await workItems.SelectAsync(x => x.Id == candidate.Id && !x.Archived, ct);
            if (workItem?.AssigneeUserId is null)
            {
                continue;
            }

            try
            {
                var authorization = await permissionChecker.EnsureCanAsync(
                    workItem.AssigneeUserId,
                    workItem.ProjectId,
                    PermissionCatalog.WorkItemView,
                    ct);
                authorizedOrganizationIds[workItem.ProjectId] = authorization.OrganizationId;
            }
            catch (Exception exception) when (exception is ForbiddenException or NotFoundException)
            {
                continue;
            }

            if (workItem.CompletedAt is not null
                || workItem.DueDate is null
                || workItem.DueDate <= now
                || workItem.DueDate > until
                || workItem.DueReminderSentAt is not null)
            {
                continue;
            }

            var deduplicationKey = $"due:{workItem.Id}:{workItem.DueDate.Value.UtcTicks}";
            await notifications.NotifyAsync(
                workItem.AssigneeUserId,
                "DueDateReminder",
                $"{workItem.Title} is due at {workItem.DueDate:O}.",
                ct,
                deduplicationKey);
            workItem.DueReminderSentAt = clock.UtcNow;
            workItem.UpdatedAt = clock.UtcNow;
            await SaveAsync(workItem, ct);
            sent++;
        }

        return sent;
    }

    private static double? TryCalculateCycleTimeHours(
        WorkItemDocument item,
        IReadOnlyCollection<WorkItemStatusHistoryResponse> statusHistory)
    {
        if (item.CompletedAt is null)
        {
            return null;
        }

        var history = statusHistory.OrderBy(x => x.ChangedAt).ToList();
        var previousCompletion = history
            .Where(x => x.ChangedAt < item.CompletedAt && IsCompletedStatus(x.ToStatus))
            .Select(x => (DateTimeOffset?)x.ChangedAt)
            .LastOrDefault();
        var startedAt = history
            .Where(x => x.ChangedAt <= item.CompletedAt
                && (!previousCompletion.HasValue || x.ChangedAt > previousCompletion.Value)
                && IsActiveStatus(x.ToStatus))
            .Select(x => (DateTimeOffset?)x.ChangedAt)
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

        var ordered = values.OrderBy(x => x).ToArray();
        var middle = ordered.Length / 2;
        var value = ordered.Length % 2 == 0
            ? (ordered[middle - 1] + ordered[middle]) / 2
            : ordered[middle];
        return Math.Round(value, 2);
    }

    private (DateOnly From, DateOnly To, DateTimeOffset FromInstant, DateTimeOffset ToInstant) NormalizeReportRange(
        DateOnly? from,
        DateOnly? to)
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

        return (
            reportFrom,
            reportTo,
            new DateTimeOffset(reportFrom.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero),
            new DateTimeOffset(reportTo.ToDateTime(TimeOnly.MaxValue), TimeSpan.Zero));
    }
}
