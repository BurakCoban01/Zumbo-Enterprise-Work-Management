using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.Modules.Projects;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Projects.Application.Features.Knowledge;

public sealed class GetKnowledgeVersionHandler(KnowledgeService service)
{
    private GetKnowledgeVersionSlice? slice;

    public GetKnowledgeVersionHandler(
        IDocumentRepository<KnowledgeDocument> documents,
        IKnowledgeDirectory directory,
        ICurrentUser currentUser)
        : this(null!)
    {
        slice = new GetKnowledgeVersionSlice(documents, directory, currentUser);
    }

    public Task<KnowledgeVersionResponse> HandleAsync(
        GetKnowledgeVersionQuery query,
        CancellationToken ct) =>
        slice?.HandleAsync(query, ct)
        ?? service.GetVersionAsync(query.DocumentId, query.Number, ct);
}
