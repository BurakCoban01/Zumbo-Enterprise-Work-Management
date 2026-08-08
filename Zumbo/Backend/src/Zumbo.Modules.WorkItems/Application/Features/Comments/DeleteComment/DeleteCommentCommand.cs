namespace Zumbo.Modules.WorkItems;

public sealed record DeleteCommentCommand(
    string Id,
    string CommentId,
    string CorrelationId);
