using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

internal sealed class EditCommentSlice(EditCommentPipeline pipeline)
{
    internal async Task<WorkItemResponse> HandleAsync(EditCommentCommand command, CancellationToken ct)
    {
        var body = WorkItemCommentRules.NormalizeBody(command.Request.Body);
        var workItem = await pipeline.LoadForEditAsync(command.Id, ct);
        var comment = workItem.Comments.SingleOrDefault(item => item.Id == command.CommentId)
            ?? throw new NotFoundException("COMMENT_NOT_FOUND", "Comment was not found.");

        if (!string.Equals(comment.AuthorUserId, pipeline.CurrentUserId, StringComparison.Ordinal))
        {
            throw new ForbiddenException("Only the comment author can edit this comment.");
        }

        if (comment.Body == body)
        {
            throw new ConflictException("COMMENT_UNCHANGED", "Comment body is unchanged.");
        }

        if (comment.History.Count >= 100)
        {
            throw new ConflictException(
                "COMMENT_HISTORY_LIMIT",
                "A comment cannot contain more than 100 revisions.");
        }

        var oldValue = comment.Body;
        var now = pipeline.UtcNow;
        comment.History.Add(new CommentRevisionDocument
        {
            Body = oldValue,
            EditedByUserId = pipeline.CurrentUserId ?? "system",
            EditedAt = now
        });
        comment.Body = body;
        comment.EditedAt = now;
        return await pipeline.PersistAndPublishAsync(
            workItem,
            comment,
            oldValue,
            command.CorrelationId,
            ct);
    }
}
