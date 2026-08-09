using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.Modules.Projects;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Projects.Application.Features.Knowledge;

public sealed class GetKnowledgeDocumentHandler(KnowledgeService service)
{
    private GetKnowledgeDocumentSlice? slice;

    public GetKnowledgeDocumentHandler(
        IDocumentRepository<KnowledgeDocument> documents,
        IKnowledgeDirectory directory,
        ICurrentUser currentUser)
        : this(null!)
    {
        slice = new GetKnowledgeDocumentSlice(documents, directory, currentUser);
    }

    public Task<KnowledgeDocumentResponse> HandleAsync(
        GetKnowledgeDocumentQuery query,
        CancellationToken ct) =>
        slice?.HandleAsync(query, ct)
        ?? service.GetAsync(query.DocumentId, query.IncludeArchived, ct);
}
