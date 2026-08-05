using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

internal sealed class MoveWorkItemPipeline(
    IDocumentRepository<WorkItemDocument> workItems,
    IWorkItemAuditPublisher audit,
    IClock clock,
    ICurrentUser currentUser,
    IProjectPermissionChecker permissionChecker,
    IBoardPlacementPolicy boardPlacementPolicy,
    IDistributedLockProvider distributedLockProvider,
    IOptions<DistributedLockOptions> distributedLockOptions,
    IWorkItemSearchPublisher searchPublisher,
    IWorkItemRealtimePublisher realtimePublisher,
    IWorkItemCacheInvalidationPublisher cacheInvalidationPublisher,
    IWorkItemActivityStore activityStore,
    WorkItemGraphService graph,
    IExpectedVersionAccessor? expectedVersions,
    WorkItemWipProjection? wipProjection,
    WorkItemCollaborationService? collaborationService,
    IWorkItemAutomationEventPublisher? automationEvents,
    IWorkItemAutomationChainContextAccessor? automationChain)
{
    private readonly Dictionary<string, string> authorizedOrganizationIds = new(StringComparer.Ordinal);
    private readonly ExpectedVersionState expectedVersion = new(expectedVersions);

    internal DateTimeOffset UtcNow => clock.UtcNow;
    internal string CurrentUserId => currentUser.UserId ?? "system";

    internal async Task<WorkItemDocument> GetWorkItemAsync(string id, CancellationToken ct)
    {
        var workItem = await workItems.SelectAsync(item => item.Id == id && !item.Archived, ct)
            ?? throw new NotFoundException("WORK_ITEM_NOT_FOUND", "Work item was not found.");
        var authorization = await EnsurePermissionAsync(
            workItem.ProjectId,
            PermissionCatalog.WorkItemView,
            ct);
        await activityStore.HydrateAsync(workItem, authorization.OrganizationId, ct);
        return workItem;
    }

    internal async Task<WorkItemDocument> GetForMoveAsync(string id, CancellationToken ct)
    {
        var workItem = await GetWorkItemAsync(id, ct);
        await EnsurePermissionAsync(workItem.ProjectId, PermissionCatalog.WorkItemMove, ct);
        await EnsureSeparatedAsync(workItem, ct);
        return workItem;
    }

    internal Task<IAsyncDisposable> AcquireStructureLockAsync(
        string projectId,
        CancellationToken ct) =>
        AcquireRequiredLockAsync("project-structure:" + projectId, ct);

    internal async Task EnsureCanCompleteAsync(WorkItemDocument workItem, CancellationToken ct)
    {
        var activeChild = await workItems.SelectAsync(
            item => item.ParentId == workItem.Id
                && !item.Archived
                && item.CompletedAt == null
                && item.Status != "Done"
                && item.Status != "Closed",
            ct);
        if (activeChild is not null)
        {
            throw new ConflictException(
                "WORK_ITEM_HAS_ACTIVE_CHILDREN",
                "Work item cannot be completed or archived while it has active children.");
        }

        var blockers = await graph.ActiveBlockerIdsAsync(workItem.ProjectId, workItem.Id, ct);
        if (blockers.Count > 0)
        {
            throw new ConflictException(
                "WORK_ITEM_BLOCKED",
                $"Work item cannot be completed while blockers remain active: {string.Join(", ", blockers)}.");
        }
    }

    internal async Task<string> PersistMoveAsync(
        WorkItemDocument workItem,
        BoardPlacement placement,
        Action applyMove,
        CancellationToken ct)
    {
        await using (await AcquirePlacementLockAsync(workItem.BoardId, placement, ct))
        {
            if (wipProjection is null)
            {
                await boardPlacementPolicy.EnsureHasCapacityAsync(
                    workItem.BoardId,
                    placement.ColumnId,
                    workItem.Id,
                    ct);
            }
            else
            {
                await wipProjection.ReserveMoveAsync(workItem, placement, ct);
            }

            var oldStatus = workItem.Status;
            applyMove();
            await SaveAsync(workItem, ct);
            foreach (var approval in workItem.Approvals)
            {
                await UpdateApprovalActivityAsync(workItem, approval, ct);
            }

            await activityStore.CreateTimelineAsync(
                WorkItemActivityStore.ToActivity(
                    workItem,
                    CurrentOrganizationId(workItem.ProjectId),
                    workItem.StatusHistory[^1],
                    workItem.StatusHistory.Count - 1),
                ct);
            return oldStatus;
        }
    }

    internal async Task PublishChangesAsync(
        WorkItemDocument workItem,
        string oldStatus,
        BoardPlacement placement,
        WorkflowTransitionRule rule,
        string correlationId,
        CancellationToken ct)
    {
        await searchPublisher.IndexAsync(
            WorkItemPublicationMapper.ToSearchRecord(
                workItem,
                CurrentOrganizationId(workItem.ProjectId)),
            ct);
        await audit.WriteAsync(
            "WorkItemMoved",
            "WorkItem",
            workItem.Id,
            oldStatus,
            placement.Status,
            correlationId,
            ct);
        await RecordActivityAndNotifyWatchersAsync(
            workItem,
            oldStatus,
            placement.Status,
            correlationId,
            ct);
        if (rule.Automations?.Count > 0)
        {
            await audit.WriteAsync(
                "WorkItemAutomationApplied",
                "WorkItem",
                workItem.Id,
                null,
                string.Join(',', rule.Automations.Select(item => item.Action)),
                correlationId,
                ct);
        }

        await PublishRealtimeAsync(workItem, correlationId, ct);
        await cacheInvalidationPublisher.InvalidateProjectAsync(workItem.ProjectId, ct);
        await PublishAutomationAsync(workItem, oldStatus, correlationId, ct);
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

    private async Task<IAsyncDisposable?> AcquirePlacementLockAsync(
        string boardId,
        BoardPlacement placement,
        CancellationToken ct) =>
        placement.EnforcesWipLimit
            ? await AcquireRequiredLockAsync($"board-column:{boardId}:{placement.ColumnId}", ct)
            : null;

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
        string oldStatus,
        string newStatus,
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
            "WorkItemMoved",
            $"{oldStatus} -> {newStatus}",
            correlationId,
            ct);
        await collaborationService.NotifyWatchersAsync(
            workItem,
            organizationId,
            "WatcherStatus",
            $"{workItem.Title} moved to {newStatus}",
            correlationId,
            null,
            ct);
    }

    private Task PublishRealtimeAsync(
        WorkItemDocument workItem,
        string correlationId,
        CancellationToken ct) =>
        realtimePublisher.PublishAsync(
            new WorkItemRealtimeChange(
                "moved",
                workItem.Id,
                workItem.ProjectId,
                workItem.BoardId,
                WorkItemPublicationMapper.ToRealtimeItem(workItem),
                correlationId,
                clock.UtcNow,
                WorkItemRealtimeProtocol.CurrentSchemaVersion,
                workItem.Version),
            ct);

    private Task PublishAutomationAsync(
        WorkItemDocument workItem,
        string oldStatus,
        string correlationId,
        CancellationToken ct)
    {
        if (automationEvents is null)
        {
            return Task.CompletedTask;
        }

        var chain = automationChain?.Current;
        var fields = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["Status"] = workItem.Status,
            ["PreviousStatus"] = oldStatus,
            ["Priority"] = workItem.Priority,
            ["Type"] = workItem.Type,
            ["AssigneeUserId"] = workItem.AssigneeUserId,
            ["Labels"] = string.Join(
                ',',
                workItem.Labels.Order(StringComparer.OrdinalIgnoreCase))
        };
        return automationEvents.PublishAsync(
            new WorkItemAutomationEvent(
                CurrentOrganizationId(workItem.ProjectId),
                workItem.ProjectId,
                "WorkItemTransitioned",
                $"{workItem.Id}:transitioned:{workItem.Version}",
                workItem.Id,
                currentUser.UserId ?? "system",
                correlationId,
                clock.UtcNow,
                fields,
                chain?.RootRunId,
                chain?.ChainDepth ?? 0,
                chain?.VisitedRuleIds ?? []),
            ct);
    }
}
