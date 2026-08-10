namespace Zumbo.Modules.Projects.Application.Features.Knowledge;

public sealed record AddKnowledgeCommentCommand(
    string DocumentId,
    AddKnowledgeCommentRequest Request,
    string CorrelationId);
