using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

internal sealed class ApprovalMutationPipeline(
    IDocumentRepository<WorkItemDocument> workItems,
    IWorkItemNotificationPublisher notifications,
    IWorkItemAuditPublisher audit,
    IClock clock,
    ICurrentUser currentUser,
    IProjectPermissionChecker permissionChecker,
    IWorkItemActivityStore activityStore,
    IExpectedVersionAccessor? expectedVersions,
    WorkItemCollaborationService? collaborationService)
{
    private readonly Dictionary<string, string> authorizedOrganizationIds = new(StringComparer.Ordinal);
    private readonly ExpectedVersionState expectedVersion = new(expectedVersions);

    internal DateTimeOffset UtcNow => clock.UtcNow;
    internal string CurrentUserId => currentUser.UserId ?? "system";

    internal async Task<WorkItemDocument> LoadForRequestAsync(string id, CancellationToken ct)
    {
        var workItem = await workItems.SelectAsync(x => x.Id == id && !x.Archived, ct)
            ?? throw new NotFoundException("WORK_ITEM_NOT_FOUND", "Work item was not found.");
        var authorization = await EnsurePermissionAsync(
            workItem.ProjectId,
            PermissionCatalog.WorkItemView,
            ct);
        await activityStore.HydrateAsync(workItem, authorization.OrganizationId, ct);
        await EnsurePermissionAsync(workItem.ProjectId, PermissionCatalog.WorkItemMove, ct);
        await EnsureSeparatedAsync(workItem, ct);
        return workItem;
    }

    internal async Task<WorkItemDocument> LoadForDecisionAsync(string id, CancellationToken ct)
    {
        var workItem = await workItems.SelectAsync(x => x.Id == id && !x.Archived, ct)
            ?? throw new NotFoundException("WORK_ITEM_NOT_FOUND", "Work item was not found.");
        var authorization = await EnsurePermissionAsync(
            workItem.ProjectId,
            PermissionCatalog.WorkItemView,
            ct);
        await activityStore.HydrateAsync(workItem, authorization.OrganizationId, ct);
        await EnsurePermissionAsync(workItem.ProjectId, PermissionCatalog.WorkItemApprove, ct);
        await EnsureSeparatedAsync(workItem, ct);
        return workItem;
    }

    internal async Task<WorkItemResponse> PersistRequestAsync(
        WorkItemDocument workItem,
        WorkItemApprovalDocument approval,
        string correlationId,
        CancellationToken ct)
    {
        await SaveAsync(workItem, ct);
        await activityStore.CreateApprovalAsync(
            WorkItemActivityStore.ToActivity(
                workItem,
                CurrentOrganizationId(workItem.ProjectId),
                approval),
            ct);
        await audit.WriteAsync(
            "WorkItemApprovalRequested",
            "WorkItem",
            workItem.Id,
            null,
            approval.Id,
            correlationId,
            ct);
        await RecordActivityAndNotifyWatchersAsync(
            workItem,
            "WorkItemApprovalRequested",
            "Approval requested",
            correlationId,
            ct);
        return WorkItemResponseMapper.ToResponse(workItem);
    }

    internal async Task PersistExpirationAsync(
        WorkItemDocument workItem,
        WorkItemApprovalDocument approval,
        string correlationId,
        CancellationToken ct)
    {
        await SaveAsync(workItem, ct);
        await UpdateApprovalActivityAsync(workItem, approval, ct);
        await RecordActivityAndNotifyWatchersAsync(
            workItem,
            "WorkItemApprovalExpired",
            "Approval expired",
            correlationId,
            ct);
    }

    internal async Task<WorkItemResponse> PersistDecisionAsync(
        WorkItemDocument workItem,
        WorkItemApprovalDocument approval,
        string correlationId,
        CancellationToken ct)
    {
        await SaveAsync(workItem, ct);
        await UpdateApprovalActivityAsync(workItem, approval, ct);
        await audit.WriteAsync(
            "WorkItemApprovalDecided",
            "WorkItem",
            workItem.Id,
            "Pending",
            approval.Status,
            correlationId,
            ct);
        await RecordActivityAndNotifyWatchersAsync(
            workItem,
            "WorkItemApprovalDecided",
            $"Approval {approval.Status.ToLowerInvariant()}",
            correlationId,
            ct);
        await notifications.NotifyWithSourceAsync(
            approval.RequestedByUserId,
            "Approval",
            $"Approval for {workItem.Title} was {approval.Status.ToLowerInvariant()}.",
            ct,
            sourceId: workItem.Id,
            projectId: workItem.ProjectId);
        return WorkItemResponseMapper.ToResponse(workItem);
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

        var authorization = await permissionChecker.EnsureCanAsync(userId, projectId, permission, ct);
        authorizedOrganizationIds[projectId] = authorization.OrganizationId;
        return authorization;
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

    private async Task EnsureSeparatedAsync(WorkItemDocument workItem, CancellationToken ct)
    {
        if (workItem.ActivityStorageVersion >= 1)
        {
            return;
        }

        await activityStore.MigrateEmbeddedAsync(
            workItem,
            CurrentOrganizationId(workItem.ProjectId),
            ct);
        await SaveAsync(workItem, ct);
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
                x => x.Id == workItem.Id,
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

    private async Task UpdateApprovalActivityAsync(
        WorkItemDocument workItem,
        WorkItemApprovalDocument approval,
        CancellationToken ct)
    {
        var organizationId = CurrentOrganizationId(workItem.ProjectId);
        var stored = WorkItemActivityStore.ToActivity(workItem, organizationId, approval);
        var current = await activityStore.GetApprovalAsync(
            organizationId,
            workItem.ProjectId,
            workItem.Id,
            approval.Id,
            ct);
        stored.Version = current?.Version
            ?? throw new NotFoundException(
                "WORK_ITEM_APPROVAL_NOT_FOUND",
                "Work item approval was not found.");
        await activityStore.UpdateApprovalAsync(stored, ct);
    }

    private async Task RecordActivityAndNotifyWatchersAsync(
        WorkItemDocument workItem,
        string activityType,
        string detail,
        string correlationId,
        CancellationToken ct)
    {
        if (collaborationService is null)
        {
            return;
        }

        var organizationId = CurrentOrganizationId(workItem.ProjectId);
        await collaborationService.RecordActivityAsync(
            workItem,
            organizationId,
            activityType,
            detail,
            correlationId,
            ct);
        await collaborationService.NotifyWatchersAsync(
            workItem,
            organizationId,
            "WatcherUpdate",
            $"{workItem.Title}: {detail}",
            correlationId,
            null,
            ct);
    }
}
