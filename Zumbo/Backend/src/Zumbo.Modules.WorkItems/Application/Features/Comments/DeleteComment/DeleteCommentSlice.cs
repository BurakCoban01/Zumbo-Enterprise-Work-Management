using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

internal sealed class DeleteCommentSlice(DeleteCommentPipeline pipeline)
{
    internal async Task<WorkItemResponse> HandleAsync(DeleteCommentCommand command, CancellationToken ct)
    {
        var workItem = await pipeline.LoadForDeleteAsync(command.Id, ct);
        var comment = workItem.Comments.SingleOrDefault(item => item.Id == command.CommentId)
            ?? throw new NotFoundException("COMMENT_NOT_FOUND", "Comment was not found.");

        if (!string.Equals(comment.AuthorUserId, pipeline.CurrentUserId, StringComparison.Ordinal))
        {
            throw new ForbiddenException("Only the comment author can delete this comment.");
        }

        return await pipeline.PersistAndPublishAsync(
            workItem,
            comment,
            command.CorrelationId,
            ct);
    }
}
