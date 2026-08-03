using System.Text.RegularExpressions;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Projects;

public sealed partial class KnowledgeService{

    private async Task ReplaceAsync(KnowledgeDocument document, CancellationToken ct)
    {
        var result = await documents.ReplaceByVersionAsync(
            item => item.Id == document.Id
                && item.OrganizationId == document.OrganizationId,
            document,
            expectedVersion.Consume(document.Version),
            ct);
        if (!result.Found)
        {
            throw new NotFoundException(
                "KNOWLEDGE_DOCUMENT_NOT_FOUND",
                "Knowledge document was not found.");
        }
        document.Version = result.Version!.Value;
    }
}
