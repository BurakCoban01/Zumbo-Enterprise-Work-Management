using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.Modules.Projects;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Projects.Application.Features.Knowledge;

internal sealed class ArchiveKnowledgeDocumentSlice(
    IDocumentRepository<KnowledgeDocument> documents,
    IKnowledgeDirectory directory,
    IKnowledgeAuditWriter audit,
    ICurrentUser currentUser,
    IClock clock,
    IExpectedVersionAccessor? expectedVersions)
{
    private readonly KnowledgeReadAccess access = new(documents, directory, currentUser);
    private readonly KnowledgeMutationPersistence persistence = new(documents, expectedVersions);

    internal async Task HandleAsync(
        ArchiveKnowledgeDocumentCommand command,
        CancellationToken ct)
    {
        var actor = access.CurrentActor();
        var document = await access.GetDocumentAsync(command.DocumentId, includeArchived: false, ct);
        var scopeAccess = await access.AuthorizeDocumentAsync(document, actor.OrganizationId, ct);
        KnowledgeVersionPolicy.EnsureCanEdit(document, scopeAccess, actor.UserId);
        document.Archived = true;
        document.UpdatedAt = clock.UtcNow;
        await persistence.ReplaceAsync(document, ct);
        await audit.WriteAsync(
            "KnowledgeDocumentArchived",
            document.Id,
            "Active",
            "Archived",
            command.CorrelationId,
            ct);
    }
}
