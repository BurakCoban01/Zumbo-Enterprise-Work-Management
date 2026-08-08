using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

internal sealed class DeleteCommentPipeline(
    IDocumentRepository<WorkItemDocument> workItems,
    IWorkItemAuditPublisher audit,
    ICurrentUser currentUser,
    IProjectPermissionChecker permissionChecker,
    IWorkItemActivityStore activityStore,
    IExpectedVersionAccessor? expectedVersions,
    WorkItemCollaborationService? collaborationService)
{
    private readonly Dictionary<string, string> authorizedOrganizationIds = new(StringComparer.Ordinal);
    private readonly ExpectedVersionState expectedVersion = new(expectedVersions);

    internal string? CurrentUserId => currentUser.UserId;

    internal async Task<WorkItemDocument> LoadForDeleteAsync(string id, CancellationToken ct)
    {
        var workItem = await workItems.SelectAsync(item => item.Id == id && !item.Archived, ct)
            ?? throw new NotFoundException("WORK_ITEM_NOT_FOUND", "Work item was not found.");
        var authorization = await EnsurePermissionAsync(
            workItem.ProjectId,
            PermissionCatalog.WorkItemView,
            ct);
        await activityStore.HydrateAsync(workItem, authorization.OrganizationId, ct);
        await EnsurePermissionAsync(workItem.ProjectId, PermissionCatalog.CommentCreate, ct);
        await EnsureSeparatedAsync(workItem, ct);
        return workItem;
    }

    internal async Task<WorkItemResponse> PersistAndPublishAsync(
        WorkItemDocument workItem,
        CommentDocument comment,
        string correlationId,
        CancellationToken ct)
    {
        var storedComment = await activityStore.GetCommentAsync(
            CurrentOrganizationId(workItem.ProjectId),
            workItem.ProjectId,
            workItem.Id,
            comment.Id,
            ct)
            ?? throw new NotFoundException("COMMENT_NOT_FOUND", "Comment was not found.");
        await activityStore.DeleteCommentAsync(storedComment, ct);
        workItem.Comments.Remove(comment);
        await audit.WriteAsync(
            "WorkItemCommentDeleted",
            "WorkItem",
            workItem.Id,
            comment.Body,
            null,
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
            "WorkItemCommentDeleted",
            "Comment deleted",
            correlationId,
            ct);
        await collaborationService.NotifyWatchersAsync(
            workItem,
            organizationId,
            "WatcherUpdate",
            $"{workItem.Title}: Comment deleted",
            correlationId,
            null,
            ct);
    }
}
