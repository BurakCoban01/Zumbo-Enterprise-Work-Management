using System.Text.RegularExpressions;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Projects;

public sealed partial class KnowledgeService{

    private async Task<KnowledgeDocument> GetDocumentAsync(
        string documentId,
        bool includeArchived,
        CancellationToken ct)
    {
        var actor = CurrentActor();
        return await documents.SelectAsync(
            item => item.Id == documentId
                && item.OrganizationId == actor.OrganizationId
                && (includeArchived || !item.Archived),
            ct)
            ?? throw new NotFoundException(
                "KNOWLEDGE_DOCUMENT_NOT_FOUND",
                "Knowledge document was not found.");
    }
}
