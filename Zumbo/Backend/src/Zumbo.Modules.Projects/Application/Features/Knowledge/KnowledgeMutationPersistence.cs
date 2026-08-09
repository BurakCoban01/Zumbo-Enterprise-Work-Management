using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.Modules.Projects;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Projects.Application.Features.Knowledge;

internal sealed class KnowledgeMutationPersistence(
    IDocumentRepository<KnowledgeDocument> documents,
    IExpectedVersionAccessor? expectedVersions)
{
    private readonly ExpectedVersionState expectedVersion = new(expectedVersions);

    internal async Task ReplaceAsync(KnowledgeDocument document, CancellationToken ct)
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
