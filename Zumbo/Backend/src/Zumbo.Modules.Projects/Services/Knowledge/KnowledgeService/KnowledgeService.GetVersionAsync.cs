using System.Text.RegularExpressions;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Projects;

public sealed partial class KnowledgeService{

    public async Task<KnowledgeVersionResponse> GetVersionAsync(
        string documentId,
        int number,
        CancellationToken ct)
    {
        var actor = CurrentActor();
        var document = await GetDocumentAsync(documentId, includeArchived: true, ct);
        _ = await AuthorizeDocumentAsync(document, actor.OrganizationId, ct);
        var version = document.Versions.SingleOrDefault(item => item.Number == number)
            ?? throw new NotFoundException(
                "KNOWLEDGE_VERSION_NOT_FOUND",
                "Knowledge document version was not found.");
        return ToResponse(version);
    }
}
