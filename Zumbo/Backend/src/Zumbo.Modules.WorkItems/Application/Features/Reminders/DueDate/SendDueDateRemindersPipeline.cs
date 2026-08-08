using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

internal sealed class SendDueDateRemindersPipeline(
    IDocumentRepository<WorkItemDocument> workItems,
    IWorkItemNotificationPublisher notifications,
    IClock clock,
    IProjectPermissionChecker permissionChecker,
    IDistributedLockProvider distributedLockProvider,
    IOptions<DistributedLockOptions> distributedLockOptions,
    IWorkItemActivityStore activityStore,
    IExpectedVersionAccessor? expectedVersions)
{
    private readonly Dictionary<string, string> authorizedOrganizationIds = new(StringComparer.Ordinal);
    private readonly ExpectedVersionState expectedVersion = new(expectedVersions);

    internal async Task<int> SendAsync(
        SendDueDateRemindersCommand command,
        CancellationToken ct)
    {
        await using var dispatcherLock = await AcquireRequiredLockAsync(
            "due-date-reminder-dispatcher",
            ct);
        var now = clock.UtcNow;
        var until = now.AddHours(Math.Clamp(command.HorizonHours, 1, 168));
        var candidates = await workItems.ListByFilterAsync(
            item => !item.Archived
                && item.CompletedAt == null
                && item.AssigneeUserId != null
                && item.DueDate != null
                && item.DueDate > now
                && item.DueDate <= until
                && item.DueReminderSentAt == null,
            item => item.DueDate!,
            pageSize: 500,
            cancellationToken: ct);
        var sent = 0;
        foreach (var candidate in candidates)
        {
            await using var workItemLock = await AcquireRequiredLockAsync(
                "work-item:" + candidate.Id,
                ct);
            var workItem = await workItems.SelectAsync(
                item => item.Id == candidate.Id && !item.Archived,
                ct);
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

    private async Task<IAsyncDisposable> AcquireRequiredLockAsync(string resource, CancellationToken ct)
    {
        var options = distributedLockOptions.Value;
        var leaseTime = TimeSpan.FromSeconds(Math.Clamp(options.LeaseSeconds, 5, 300));
        var waitTime = TimeSpan.FromSeconds(Math.Clamp(options.WaitSeconds, 0, 30));
        return await distributedLockProvider.TryAcquireAsync(resource, leaseTime, waitTime, ct)
            ?? throw new ConflictException(
                "RESOURCE_BUSY",
                "The requested resource is busy; retry the operation.");
    }

    private string CurrentOrganizationId(string projectId)
    {
        if (!authorizedOrganizationIds.TryGetValue(projectId, out var organizationId))
        {
            throw new InvalidOperationException(
                "Project resource must be authorized before tenant data is accessed.");
        }

        return organizationId;
    }

    private async Task SaveAsync(WorkItemDocument workItem, CancellationToken ct)
    {
        await activityStore.MigrateEmbeddedAsync(
            workItem,
            CurrentOrganizationId(workItem.ProjectId),
            ct);
        var comments = workItem.Comments;
        var attachments = workItem.Attachments;
        var workLogs = workItem.WorkLogs;
        var approvals = workItem.Approvals;
        var statusHistory = workItem.StatusHistory;
        workItem.Comments = [];
        workItem.Attachments = [];
        workItem.WorkLogs = [];
        workItem.Approvals = [];
        workItem.StatusHistory = [];
        try
        {
            var result = await workItems.ReplaceByVersionAsync(
                item => item.Id == workItem.Id,
                workItem,
                expectedVersion.Consume(workItem.Version),
                ct);
            if (!result.Found)
            {
                throw new NotFoundException("WORK_ITEM_NOT_FOUND", "Work item was not found.");
            }

            workItem.Version = result.Version!.Value;
        }
        finally
        {
            workItem.Comments = comments;
            workItem.Attachments = attachments;
            workItem.WorkLogs = workLogs;
            workItem.Approvals = approvals;
            workItem.StatusHistory = statusHistory;
        }
    }
}
