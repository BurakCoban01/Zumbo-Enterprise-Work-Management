using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

internal sealed class SetParentPipeline(
    IDocumentRepository<WorkItemDocument> workItems,
    IWorkItemAuditPublisher audit,
    IClock clock,
    ICurrentUser currentUser,
    IProjectPermissionChecker permissionChecker,
    IWorkItemTypeSchemaPolicy typeSchemas,
    IDistributedLockProvider distributedLockProvider,
    IOptions<DistributedLockOptions> distributedLockOptions,
    IWorkItemActivityStore activityStore,
    WorkItemGraphService graph,
    IExpectedVersionAccessor? expectedVersions,
    WorkItemCollaborationService? collaborationService)
{
    private readonly Dictionary<string, string> authorizedOrganizationIds = new(StringComparer.Ordinal);
    private readonly ExpectedVersionState expectedVersion = new(expectedVersions);

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

    internal async Task<WorkItemDocument> GetForSetParentAsync(string id, CancellationToken ct)
    {
        var workItem = await GetWorkItemAsync(id, ct);
        await EnsurePermissionAsync(workItem.ProjectId, PermissionCatalog.WorkItemUpdate, ct);
        return workItem;
    }

    internal Task<IAsyncDisposable> AcquireStructureLockAsync(
        string projectId,
        CancellationToken ct) =>
        AcquireRequiredLockAsync("project-structure:" + projectId, ct);

    internal async Task<WorkItemDocument?> ValidateParentAsync(
        string projectId,
        string boardId,
        string type,
        string? parentId,
        string? workItemId,
        CancellationToken ct)
    {
        var hierarchyLevel = await typeSchemas.HierarchyLevelAsync(projectId, type, ct);
        if (string.IsNullOrWhiteSpace(parentId))
        {
            if (hierarchyLevel == IssueTypeHierarchyLevels.Subtask)
            {
                throw new ValidationException("A subtask must have a parent work item.");
            }

            return null;
        }

        if (hierarchyLevel == IssueTypeHierarchyLevels.Epic)
        {
            throw new ValidationException("An epic cannot have a parent work item.");
        }

        if (string.Equals(parentId, workItemId, StringComparison.Ordinal))
        {
            throw new ValidationException("A work item cannot be its own parent.");
        }

        var parent = await GetWorkItemAsync(parentId, ct);
        if (!string.Equals(parent.ProjectId, projectId, StringComparison.Ordinal))
        {
            throw new ValidationException("A parent work item must belong to the same project.");
        }

        if (parent.CompletedAt is not null || IsCompletedStatus(parent.Status))
        {
            throw new ConflictException(
                "WORK_ITEM_PARENT_COMPLETED",
                "A completed work item cannot receive a child.");
        }

        var parentHierarchy = await typeSchemas.HierarchyLevelAsync(projectId, parent.Type, ct);
        if (hierarchyLevel == IssueTypeHierarchyLevels.Subtask)
        {
            if (parentHierarchy != IssueTypeHierarchyLevels.Standard)
            {
                throw new ValidationException("A subtask parent must be a story, task or bug.");
            }

            if (!string.Equals(parent.BoardId, boardId, StringComparison.Ordinal))
            {
                throw new ValidationException("A subtask and its parent must belong to the same board.");
            }
        }
        else if (parentHierarchy != IssueTypeHierarchyLevels.Epic)
        {
            throw new ValidationException("A story, task or bug can only be parented by an epic.");
        }

        await graph.EnsureCanSetParentAsync(projectId, workItemId, parent.Id, ct);
        return parent;
    }

    internal async Task<WorkItemResponse> PersistAsync(
        WorkItemDocument workItem,
        string? oldParentId,
        string? parentId,
        string correlationId,
        CancellationToken ct)
    {
        workItem.ParentId = parentId;
        workItem.UpdatedAt = clock.UtcNow;
        await SaveAsync(workItem, ct);
        await audit.WriteAsync(
            "WorkItemParentChanged",
            "WorkItem",
            workItem.Id,
            oldParentId,
            parentId,
            correlationId,
            ct);
        await RecordActivityAndNotifyWatchersAsync(workItem, correlationId, ct);
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
            "WorkItemParentChanged",
            "Parent changed",
            correlationId,
            ct);
        await collaborationService.NotifyWatchersAsync(
            workItem,
            organizationId,
            "WatcherUpdate",
            $"{workItem.Title}: Parent changed",
            correlationId,
            null,
            ct);
    }

    private static bool IsCompletedStatus(string status) =>
        status.Equals("Done", StringComparison.OrdinalIgnoreCase)
        || status.Equals("Closed", StringComparison.OrdinalIgnoreCase);
}
