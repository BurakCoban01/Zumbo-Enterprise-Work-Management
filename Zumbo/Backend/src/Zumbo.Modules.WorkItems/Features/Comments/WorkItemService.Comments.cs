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
    public async Task<WorkItemResponse> AddCommentAsync(string id, AddCommentRequest request, string correlationId, CancellationToken ct)
    {
        var body = WorkItemCommentRules.NormalizeBody(request.Body);
        var mentions = WorkItemCommentRules.NormalizeMentions(request.Mentions);
        var idempotencyKey = WorkItemCommentRules.NormalizeIdempotencyKey(request.IdempotencyKey);
        var workItem = await GetWorkItem(id, ct);
        await EnsurePermissionAsync(workItem.ProjectId, "CommentCreate", ct);
        var organizationId = CurrentOrganizationId(workItem.ProjectId);
        if (collaborationService is not null)
        {
            await collaborationService.ValidateMentionsAsync(
                organizationId,
                workItem.ProjectId,
                mentions,
                ct);
        }
        await EnsureSeparatedAsync(workItem, ct);
        var authorUserId = currentUser.UserId ?? "system";
        var stableCommentId = idempotencyKey is null
            ? null
            : IntakeStableIds.Hash(
                $"comment\u001f{workItem.Id}\u001f{authorUserId}\u001f{idempotencyKey}")[..32];
        if (stableCommentId is not null
            && workItem.Comments.SingleOrDefault(comment => comment.Id == stableCommentId) is { } existing)
        {
            if (existing.Body != body
                || !existing.Mentions.Order(StringComparer.Ordinal)
                    .SequenceEqual(mentions.Order(StringComparer.Ordinal)))
            {
                throw new ConflictException(
                    "COMMENT_IDEMPOTENCY_KEY_REUSED",
                    "Idempotency key was already used for a different comment.");
            }

            return ToResponse(workItem);
        }
        if (workItem.Comments.Count >= 500)
        {
            throw new ConflictException("WORK_ITEM_COMMENT_LIMIT", "A work item cannot contain more than 500 comments.");
        }

        var comment = new CommentDocument
        {
            Id = stableCommentId ?? Guid.NewGuid().ToString("N"),
            Body = body,
            AuthorUserId = authorUserId,
            Mentions = mentions,
            CreatedAt = clock.UtcNow
        };

        await activityStore.CreateCommentAsync(
            WorkItemActivityStore.ToActivity(workItem, CurrentOrganizationId(workItem.ProjectId), comment),
            ct);
        workItem.Comments.Add(comment);
        await audit.WriteAsync("WorkItemCommentAdded", "WorkItem", workItem.Id, null, comment.Id, correlationId, ct);

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

        await PublishAutomationAsync(
            "WorkItemUpdated",
            workItem,
            workItem.Status,
            correlationId,
            $"comment-added:{comment.Id}",
            ct);
        return ToResponse(workItem);
    }

    public async Task<WorkItemResponse> EditCommentAsync(string id, string commentId, EditCommentRequest request, string correlationId, CancellationToken ct)
    {
        var body = WorkItemCommentRules.NormalizeBody(request.Body);
        var workItem = await GetWorkItem(id, ct);
        await EnsurePermissionAsync(workItem.ProjectId, "CommentCreate", ct);
        await EnsureSeparatedAsync(workItem, ct);
        var comment = workItem.Comments.SingleOrDefault(x => x.Id == commentId)
            ?? throw new NotFoundException("COMMENT_NOT_FOUND", "Comment was not found.");

        if (!string.Equals(comment.AuthorUserId, currentUser.UserId, StringComparison.Ordinal))
        {
            throw new ForbiddenException("Only the comment author can edit this comment.");
        }

        if (comment.Body == body)
        {
            throw new ConflictException("COMMENT_UNCHANGED", "Comment body is unchanged.");
        }

        if (comment.History.Count >= 100)
        {
            throw new ConflictException("COMMENT_HISTORY_LIMIT", "A comment cannot contain more than 100 revisions.");
        }

        var oldValue = comment.Body;
        var now = clock.UtcNow;
        comment.History.Add(new CommentRevisionDocument
        {
            Body = oldValue,
            EditedByUserId = currentUser.UserId ?? "system",
            EditedAt = now
        });
        comment.Body = body;
        comment.EditedAt = now;
        var organizationId = CurrentOrganizationId(workItem.ProjectId);
        var storedComment = await activityStore.GetCommentAsync(
            organizationId, workItem.ProjectId, workItem.Id, comment.Id, ct)
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
        await audit.WriteAsync("WorkItemCommentEdited", "WorkItem", workItem.Id, oldValue, comment.Id, correlationId, ct);
        await RecordActivityAndNotifyWatchersAsync(
            workItem,
            "WorkItemCommentEdited",
            "Comment edited",
            $"comment:{commentId}:revision:{comment.History.Count}",
            ct);
        return ToResponse(workItem);
    }

    public async Task<WorkItemResponse> DeleteCommentAsync(string id, string commentId, string correlationId, CancellationToken ct)
    {
        var workItem = await GetWorkItem(id, ct);
        await EnsurePermissionAsync(workItem.ProjectId, "CommentCreate", ct);
        await EnsureSeparatedAsync(workItem, ct);
        var comment = workItem.Comments.SingleOrDefault(x => x.Id == commentId)
            ?? throw new NotFoundException("COMMENT_NOT_FOUND", "Comment was not found.");

        if (!string.Equals(comment.AuthorUserId, currentUser.UserId, StringComparison.Ordinal))
        {
            throw new ForbiddenException("Only the comment author can delete this comment.");
        }

        var storedComment = await activityStore.GetCommentAsync(
            CurrentOrganizationId(workItem.ProjectId), workItem.ProjectId, workItem.Id, comment.Id, ct)
            ?? throw new NotFoundException("COMMENT_NOT_FOUND", "Comment was not found.");
        await activityStore.DeleteCommentAsync(storedComment, ct);
        workItem.Comments.Remove(comment);
        await audit.WriteAsync("WorkItemCommentDeleted", "WorkItem", workItem.Id, comment.Body, null, correlationId, ct);
        await RecordActivityAndNotifyWatchersAsync(
            workItem, "WorkItemCommentDeleted", "Comment deleted", correlationId, ct);
        return ToResponse(workItem);
    }
}
