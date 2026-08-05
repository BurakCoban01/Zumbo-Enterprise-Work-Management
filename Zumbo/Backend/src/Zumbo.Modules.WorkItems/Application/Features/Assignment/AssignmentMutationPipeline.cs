using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

internal sealed class AssignmentMutationPipeline(
    IDocumentRepository<WorkItemDocument> workItems,
    IWorkItemAuditPublisher audit,
    IClock clock,
    ICurrentUser currentUser,
    IProjectPermissionChecker permissionChecker,
    IWorkItemSearchPublisher searchPublisher,
    IWorkItemRealtimePublisher realtimePublisher,
    IWorkItemCacheInvalidationPublisher cacheInvalidationPublisher,
    IWorkItemActivityStore activityStore,
    IExpectedVersionAccessor? expectedVersions,
    WorkItemCollaborationService? collaborationService,
    IWorkItemAutomationEventPublisher? automationEvents,
    IWorkItemAutomationChainContextAccessor? automationChain)
{
    private readonly Dictionary<string, string> authorizedOrganizationIds = new(StringComparer.Ordinal);
    private readonly ExpectedVersionState expectedVersion = new(expectedVersions);

    internal Task<WorkItemDocument> LoadForAssignmentAsync(string id, CancellationToken ct) =>
        LoadForMutationAsync(id, PermissionCatalog.WorkItemAssign, ct);

    internal Task<WorkItemDocument> LoadForTeamUpdateAsync(string id, CancellationToken ct) =>
        LoadForMutationAsync(id, PermissionCatalog.WorkItemUpdate, ct);

    private async Task<WorkItemDocument> LoadForMutationAsync(
        string id,
        string permission,
        CancellationToken ct)
    {
        var workItem = await workItems.SelectAsync(x => x.Id == id && !x.Archived, ct)
            ?? throw new NotFoundException("WORK_ITEM_NOT_FOUND", "Work item was not found.");
        var authorization = await EnsurePermissionAsync(
            workItem.ProjectId,
            PermissionCatalog.WorkItemView,
            ct);
        await activityStore.HydrateAsync(workItem, authorization.OrganizationId, ct);
        await EnsurePermissionAsync(workItem.ProjectId, permission, ct);
        return workItem;
    }

    internal async Task<WorkItemResponse> PersistClearAsync(
        WorkItemDocument workItem,
        string oldAssignee,
        string correlationId,
        CancellationToken ct)
    {
        workItem.UpdatedAt = clock.UtcNow;
        await SaveAsync(workItem, ct);
        await searchPublisher.IndexAsync(
            WorkItemPublicationMapper.ToSearchRecord(
                workItem,
                CurrentOrganizationId(workItem.ProjectId)),
            ct);
        await audit.WriteAsync(
            "WorkItemAssigneeCleared",
            "WorkItem",
            workItem.Id,
            oldAssignee,
            null,
            correlationId,
            ct);
        await RecordActivityAndNotifyWatchersAsync(
            workItem,
            "WorkItemAssigneeCleared",
            "Assignee cleared",
            correlationId,
            ct);
        await PublishRealtimeAsync("updated", workItem, correlationId, ct);
        await cacheInvalidationPublisher.InvalidateProjectAsync(workItem.ProjectId, ct);
        await PublishAutomationAsync(
            "WorkItemUpdated",
            workItem,
            workItem.Status,
            correlationId,
            $"assignee-cleared:{workItem.Version}",
            ct);
        return WorkItemResponseMapper.ToResponse(workItem);
    }

    internal async Task<WorkItemResponse> PersistAssignmentAsync(
        WorkItemDocument workItem,
        string? oldAssignee,
        string assigneeUserId,
        string correlationId,
        IWorkItemNotificationPublisher notifications,
        CancellationToken ct)
    {
        workItem.UpdatedAt = clock.UtcNow;
        await SaveAsync(workItem, ct);
        await searchPublisher.IndexAsync(
            WorkItemPublicationMapper.ToSearchRecord(
                workItem,
                CurrentOrganizationId(workItem.ProjectId)),
            ct);
        await audit.WriteAsync(
            "WorkItemAssigned",
            "WorkItem",
            workItem.Id,
            oldAssignee,
            assigneeUserId,
            correlationId,
            ct);
        await notifications.NotifyAsync(
            assigneeUserId,
            "Assignment",
            $"Assigned to {workItem.Title}",
            ct);
        if (collaborationService is not null)
        {
            var organizationId = CurrentOrganizationId(workItem.ProjectId);
            await collaborationService.RecordActivityAsync(
                workItem,
                organizationId,
                "WorkItemAssigned",
                "Assignee changed",
                correlationId,
                ct);
            await collaborationService.NotifyWatchersAsync(
                workItem,
                organizationId,
                "WatcherAssignment",
                $"The assignee changed on {workItem.Title}",
                correlationId,
                [assigneeUserId],
                ct);
        }

        await PublishRealtimeAsync("updated", workItem, correlationId, ct);
        await cacheInvalidationPublisher.InvalidateProjectAsync(workItem.ProjectId, ct);
        return WorkItemResponseMapper.ToResponse(workItem);
    }

    internal async Task<WorkItemResponse> PersistTeamChangeAsync(
        WorkItemDocument workItem,
        string? oldTeamId,
        string? teamId,
        string correlationId,
        CancellationToken ct)
    {
        workItem.UpdatedAt = clock.UtcNow;
        await SaveAsync(workItem, ct);
        await audit.WriteAsync(
            "WorkItemTeamChanged",
            "WorkItem",
            workItem.Id,
            oldTeamId,
            teamId,
            correlationId,
            ct);
        await RecordActivityAndNotifyWatchersAsync(
            workItem,
            "WorkItemTeamChanged",
            "Team changed",
            correlationId,
            ct);
        await PublishRealtimeAsync("updated", workItem, correlationId, ct);
        await cacheInvalidationPublisher.InvalidateProjectAsync(workItem.ProjectId, ct);
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

    private async Task RecordActivityAndNotifyWatchersAsync(
        WorkItemDocument workItem,
        string activityType,
        string detail,
        string eventId,
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
            eventId,
            ct);
        await collaborationService.NotifyWatchersAsync(
            workItem,
            organizationId,
            "WatcherUpdate",
            $"{workItem.Title}: {detail}",
            eventId,
            null,
            ct);
    }

    private Task PublishRealtimeAsync(
        string eventType,
        WorkItemDocument workItem,
        string correlationId,
        CancellationToken ct) =>
        realtimePublisher.PublishAsync(
            new WorkItemRealtimeChange(
                eventType,
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
        string eventType,
        WorkItemDocument workItem,
        string? previousStatus,
        string correlationId,
        string mutationId,
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
            ["PreviousStatus"] = previousStatus,
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
                eventType,
                $"{workItem.Id}:{mutationId}",
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
