using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.Modules.Projects;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Projects.Application.Features.Knowledge;

internal sealed class GetKnowledgeDocumentSlice(
    IDocumentRepository<KnowledgeDocument> documents,
    IKnowledgeDirectory directory,
    ICurrentUser currentUser)
{
    private readonly KnowledgeReadAccess access = new(documents, directory, currentUser);

    internal async Task<KnowledgeDocumentResponse> HandleAsync(
        GetKnowledgeDocumentQuery query,
        CancellationToken ct)
    {
        var actor = access.CurrentActor();
        var document = await access.GetDocumentAsync(query.DocumentId, query.IncludeArchived, ct);
        var scopeAccess = await access.AuthorizeDocumentAsync(document, actor.OrganizationId, ct);
        return KnowledgeResponseMapper.ToDocument(document, scopeAccess, actor.UserId);
    }
}
