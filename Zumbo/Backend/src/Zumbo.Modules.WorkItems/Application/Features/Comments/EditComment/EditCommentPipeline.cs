using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

internal sealed class EditCommentPipeline(
    IDocumentRepository<WorkItemDocument> workItems,
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
    internal string? CurrentUserId => currentUser.UserId;

    internal async Task<WorkItemDocument> LoadForEditAsync(string id, CancellationToken ct)
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
        string oldValue,
        string correlationId,
        CancellationToken ct)
    {
        var organizationId = CurrentOrganizationId(workItem.ProjectId);
        var storedComment = await activityStore.GetCommentAsync(
            organizationId,
            workItem.ProjectId,
            workItem.Id,
            comment.Id,
            ct)
            ?? throw new NotFoundException("COMMENT_NOT_FOUND", "Comment was not found.");
        var revision = WorkItemActivityStore.ToActivity(
            workItem,
            organizationId,
            comment.Id,
            comment.History[^1],
            comment.History.Count - 1);
        await activityStore.CreateRevisionAsync(revision, ct);
        storedComment.Body = comment.Body;
        storedComment.EditedAt = comment.EditedAt;
        await activityStore.UpdateCommentAsync(storedComment, ct);
        await audit.WriteAsync(
            "WorkItemCommentEdited",
            "WorkItem",
            workItem.Id,
            oldValue,
            comment.Id,
            correlationId,
            ct);
        await RecordActivityAndNotifyWatchersAsync(
            workItem,
            comment.Id,
            comment.History.Count,
            ct);
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
        string commentId,
        int revisionCount,
        CancellationToken ct)
    {
        if (collaborationService is null)
        {
            return;
        }

        var organizationId = CurrentOrganizationId(workItem.ProjectId);
        var eventId = $"comment:{commentId}:revision:{revisionCount}";
        await collaborationService.RecordActivityAsync(
            workItem,
            organizationId,
            "WorkItemCommentEdited",
            "Comment edited",
            eventId,
            ct);
        await collaborationService.NotifyWatchersAsync(
            workItem,
            organizationId,
            "WatcherUpdate",
            $"{workItem.Title}: Comment edited",
            eventId,
            null,
            ct);
    }
}
