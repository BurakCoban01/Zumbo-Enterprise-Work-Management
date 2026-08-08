namespace Zumbo.Modules.WorkItems;

public sealed record EditCommentCommand(
    string Id,
    string CommentId,
    EditCommentRequest Request,
    string CorrelationId);
