using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

internal sealed class AddCommentPipeline(
    IDocumentRepository<WorkItemDocument> workItems,
    IWorkItemNotificationPublisher notifications,
    IWorkItemAuditPublisher audit,
    IClock clock,
    ICurrentUser currentUser,
    IProjectPermissionChecker permissionChecker,
    IWorkItemActivityStore activityStore,
    IExpectedVersionAccessor? expectedVersions,
    WorkItemCollaborationService? collaborationService,
    IWorkItemAutomationEventPublisher? automationEvents,
    IWorkItemAutomationChainContextAccessor? automationChain)
{
    private readonly Dictionary<string, string> authorizedOrganizationIds = new(StringComparer.Ordinal);
    private readonly ExpectedVersionState expectedVersion = new(expectedVersions);

    internal DateTimeOffset UtcNow => clock.UtcNow;
    internal string CurrentUserId => currentUser.UserId ?? "system";

    internal async Task<WorkItemDocument> LoadForCreateAsync(
        string id,
        IReadOnlyCollection<string> mentions,
        CancellationToken ct)
    {
        var workItem = await workItems.SelectAsync(item => item.Id == id && !item.Archived, ct)
            ?? throw new NotFoundException("WORK_ITEM_NOT_FOUND", "Work item was not found.");
        var authorization = await EnsurePermissionAsync(
            workItem.ProjectId,
            PermissionCatalog.WorkItemView,
            ct);
        await activityStore.HydrateAsync(workItem, authorization.OrganizationId, ct);
        await EnsurePermissionAsync(workItem.ProjectId, PermissionCatalog.CommentCreate, ct);
        if (collaborationService is not null)
        {
            await collaborationService.ValidateMentionsAsync(
                CurrentOrganizationId(workItem.ProjectId),
                workItem.ProjectId,
                mentions,
                ct);
        }

        await EnsureSeparatedAsync(workItem, ct);
        return workItem;
    }

    internal async Task<WorkItemResponse> PersistAndPublishAsync(
        WorkItemDocument workItem,
        CommentDocument comment,
        string correlationId,
        CancellationToken ct)
    {
        var organizationId = CurrentOrganizationId(workItem.ProjectId);
        await activityStore.CreateCommentAsync(
            WorkItemActivityStore.ToActivity(workItem, organizationId, comment),
            ct);
        workItem.Comments.Add(comment);
        await audit.WriteAsync(
            "WorkItemCommentAdded",
            "WorkItem",
            workItem.Id,
            null,
            comment.Id,
            correlationId,
            ct);

        foreach (var mentionedUserId in comment.Mentions)
        {
            if (mentionedUserId != currentUser.UserId)
            {
                await notifications.NotifyAsync(
                    mentionedUserId,
                    "Mention",
                    $"Mentioned on {workItem.Title}",
                    ct,
                    $"mention:{workItem.Id}:{comment.Id}:{mentionedUserId}");
            }
        }

        if (collaborationService is not null)
        {
            await collaborationService.RecordActivityAsync(
                workItem,
                organizationId,
                "WorkItemCommentAdded",
                "Comment added",
                comment.Id,
                ct);
            await collaborationService.NotifyWatchersAsync(
                workItem,
                organizationId,
                "WatcherComment",
                $"A comment was added to {workItem.Title}",
                comment.Id,
                comment.Mentions,
                ct);
        }

        await PublishAutomationAsync(workItem, comment.Id, correlationId, ct);
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

    private Task PublishAutomationAsync(
        WorkItemDocument workItem,
        string commentId,
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
                $"{workItem.Id}:comment-added:{commentId}",
                workItem.Id,
                CurrentUserId,
                correlationId,
                clock.UtcNow,
                fields,
                chain?.RootRunId,
                chain?.ChainDepth ?? 0,
                chain?.VisitedRuleIds ?? []),
            ct);
    }
}
