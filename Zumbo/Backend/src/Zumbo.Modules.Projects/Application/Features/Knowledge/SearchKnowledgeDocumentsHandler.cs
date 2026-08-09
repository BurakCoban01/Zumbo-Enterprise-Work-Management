using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.Modules.Projects;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Projects.Application.Features.Knowledge;

public sealed class SearchKnowledgeDocumentsHandler(KnowledgeService service)
{
    private SearchKnowledgeDocumentsSlice? slice;

    public SearchKnowledgeDocumentsHandler(
        IDocumentRepository<KnowledgeDocument> documents,
        IKnowledgeDirectory directory,
        ICurrentUser currentUser)
        : this(null!)
    {
        slice = new SearchKnowledgeDocumentsSlice(documents, directory, currentUser);
    }

    public Task<KnowledgeSearchResponse> HandleAsync(
        SearchKnowledgeDocumentsQuery query,
        CancellationToken ct) =>
        slice?.HandleAsync(query, ct)
        ?? service.SearchAsync(
            query.Query,
            query.ScopeType,
            query.ScopeId,
            query.IncludeArchived,
            query.Page,
            query.PageSize,
            ct);
}
