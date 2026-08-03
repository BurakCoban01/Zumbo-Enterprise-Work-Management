using System.Text.RegularExpressions;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Projects;

public sealed partial class KnowledgeService{

    public async Task<KnowledgeDocumentResponse> GetAsync(
        string documentId,
        bool includeArchived,
        CancellationToken ct)
    {
        var actor = CurrentActor();
        var document = await GetDocumentAsync(documentId, includeArchived, ct);
        var access = await AuthorizeDocumentAsync(document, actor.OrganizationId, ct);
        return ToResponse(document, access, actor.UserId);
    }
}
