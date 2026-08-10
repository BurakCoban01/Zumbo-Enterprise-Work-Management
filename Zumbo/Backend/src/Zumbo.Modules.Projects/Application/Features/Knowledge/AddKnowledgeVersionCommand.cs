namespace Zumbo.Modules.Projects.Application.Features.Knowledge;

public sealed record AddKnowledgeVersionCommand(
    string DocumentId,
    CreateKnowledgeVersionRequest Request,
    string CorrelationId);
