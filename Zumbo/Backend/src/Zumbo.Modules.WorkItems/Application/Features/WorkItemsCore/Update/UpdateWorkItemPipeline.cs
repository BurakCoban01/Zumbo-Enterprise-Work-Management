using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

internal sealed class UpdateWorkItemPipeline(
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

    internal async Task<WorkItemDocument> LoadAsync(string id, CancellationToken ct)
    {
        var workItem = await workItems.SelectAsync(item => item.Id == id && !item.Archived, ct)
            ?? throw new NotFoundException("WORK_ITEM_NOT_FOUND", "Work item was not found.");
        var authorization = await EnsurePermissionAsync(
            workItem.ProjectId,
            PermissionCatalog.WorkItemView,
            ct);
        await activityStore.HydrateAsync(workItem, authorization.OrganizationId, ct);
        await EnsurePermissionAsync(workItem.ProjectId, PermissionCatalog.WorkItemUpdate, ct);
        return workItem;
    }

    internal async Task<WorkItemResponse> PersistAsync(
        WorkItemDocument workItem,
        string oldValue,
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
            "WorkItemUpdated",
            "WorkItem",
            workItem.Id,
            oldValue,
            AuditValue(workItem),
            correlationId,
            ct);
        await RecordActivityAndNotifyWatchersAsync(workItem, correlationId, ct);
        await PublishRealtimeAsync(workItem, correlationId, ct);
        await cacheInvalidationPublisher.InvalidateProjectAsync(workItem.ProjectId, ct);
        await PublishAutomationAsync(workItem, correlationId, ct);
        return WorkItemResponseMapper.ToResponse(workItem);
    }

    internal static string AuditValue(WorkItemDocument workItem) =>
        $"{workItem.Title}|{workItem.Priority}|{workItem.DueDate:o}";

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

    private async Task RecordActivityAndNotifyWatchersAsync(
        WorkItemDocument workItem,
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
            "WorkItemUpdated",
            "Fields updated",
            correlationId,
            ct);
        await collaborationService.NotifyWatchersAsync(
            workItem,
            organizationId,
            "WatcherUpdate",
            $"{workItem.Title} was updated",
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
                "updated",
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
            ["PreviousStatus"] = workItem.Status,
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
                "WorkItemUpdated",
                $"{workItem.Id}:updated:{workItem.Version}",
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
