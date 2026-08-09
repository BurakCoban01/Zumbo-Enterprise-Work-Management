using Zumbo.Modules.Projects.Application.Features.Knowledge;

namespace Zumbo.Modules.Projects;

public sealed partial class KnowledgeService
{
    public async Task<KnowledgeDocumentResponse> GetAsync(
        string documentId,
        bool includeArchived,
        CancellationToken ct) =>
        await getKnowledgeDocumentHandler.HandleAsync(
            new GetKnowledgeDocumentQuery(documentId, includeArchived),
            ct);

    public async Task<KnowledgeVersionResponse> GetVersionAsync(
        string documentId,
        int number,
        CancellationToken ct) =>
        await getKnowledgeVersionHandler.HandleAsync(
            new GetKnowledgeVersionQuery(documentId, number),
            ct);

    public async Task<KnowledgeLinkOptionsResponse> GetLinkOptionsAsync(
        string scopeType,
        string scopeId,
        string? query,
        CancellationToken ct) =>
        await getKnowledgeLinkOptionsHandler.HandleAsync(
            new GetKnowledgeLinkOptionsQuery(scopeType, scopeId, query),
            ct);

    public async Task<KnowledgeSearchResponse> SearchAsync(
        string? query,
        string? scopeType,
        string? scopeId,
        bool includeArchived,
        int page,
        int pageSize,
        CancellationToken ct) =>
        await searchKnowledgeDocumentsHandler.HandleAsync(
            new SearchKnowledgeDocumentsQuery(
                query,
                scopeType,
                scopeId,
                includeArchived,
                page,
                pageSize),
            ct);
}
