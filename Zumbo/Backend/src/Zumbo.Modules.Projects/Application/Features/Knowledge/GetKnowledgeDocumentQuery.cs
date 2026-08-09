namespace Zumbo.Modules.Projects.Application.Features.Knowledge;

public sealed record GetKnowledgeDocumentQuery(
    string DocumentId,
    bool IncludeArchived);
