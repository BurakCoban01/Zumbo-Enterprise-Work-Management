namespace Zumbo.Modules.Projects.Application.Features.Knowledge;

public sealed record SearchKnowledgeDocumentsQuery(
    string? Query,
    string? ScopeType,
    string? ScopeId,
    bool IncludeArchived,
    int Page,
    int PageSize);
