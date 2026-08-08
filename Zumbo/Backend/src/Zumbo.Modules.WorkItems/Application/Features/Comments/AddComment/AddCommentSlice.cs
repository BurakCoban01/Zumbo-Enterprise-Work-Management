using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

internal sealed class AddCommentSlice(AddCommentPipeline pipeline)
{
    internal async Task<WorkItemResponse> HandleAsync(AddCommentCommand command, CancellationToken ct)
    {
        var body = WorkItemCommentRules.NormalizeBody(command.Request.Body);
        var mentions = WorkItemCommentRules.NormalizeMentions(command.Request.Mentions);
        var idempotencyKey = WorkItemCommentRules.NormalizeIdempotencyKey(
            command.Request.IdempotencyKey);
        var workItem = await pipeline.LoadForCreateAsync(command.Id, mentions, ct);
        var authorUserId = pipeline.CurrentUserId;
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

            return WorkItemResponseMapper.ToResponse(workItem);
        }

        if (workItem.Comments.Count >= 500)
        {
            throw new ConflictException(
                "WORK_ITEM_COMMENT_LIMIT",
                "A work item cannot contain more than 500 comments.");
        }

        var comment = new CommentDocument
        {
            Id = stableCommentId ?? Guid.NewGuid().ToString("N"),
            Body = body,
            AuthorUserId = authorUserId,
            Mentions = mentions,
            CreatedAt = pipeline.UtcNow
        };
        return await pipeline.PersistAndPublishAsync(
            workItem,
            comment,
            command.CorrelationId,
            ct);
    }
}
