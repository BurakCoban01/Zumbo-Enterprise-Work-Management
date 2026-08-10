using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.Modules.Projects;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Projects.Application.Features.Knowledge;

internal sealed class GetKnowledgeVersionSlice(
    IDocumentRepository<KnowledgeDocument> documents,
    IKnowledgeDirectory directory,
    ICurrentUser currentUser)
{
    private readonly KnowledgeReadAccess access = new(documents, directory, currentUser);

    internal async Task<KnowledgeVersionResponse> HandleAsync(
        GetKnowledgeVersionQuery query,
        CancellationToken ct)
    {
        var actor = access.CurrentActor();
        var document = await access.GetDocumentAsync(query.DocumentId, includeArchived: true, ct);
        _ = await access.AuthorizeDocumentAsync(document, actor.OrganizationId, ct);
        var version = document.Versions.SingleOrDefault(item => item.Number == query.Number)
            ?? throw new NotFoundException(
                "KNOWLEDGE_VERSION_NOT_FOUND",
                "Knowledge document version was not found.");
        return KnowledgeResponseMapper.ToVersion(version);
    }
}
