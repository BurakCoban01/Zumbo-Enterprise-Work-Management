namespace Zumbo.Modules.Projects.Application.Features.Knowledge;

public sealed record ResolveKnowledgeCommentCommand(
    string DocumentId,
    string CommentId,
    string CorrelationId);
