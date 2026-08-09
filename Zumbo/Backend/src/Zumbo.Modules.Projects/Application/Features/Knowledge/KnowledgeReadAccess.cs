using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.Modules.Projects;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Projects.Application.Features.Knowledge;

internal sealed class KnowledgeReadAccess(
    IDocumentRepository<KnowledgeDocument> documents,
    IKnowledgeDirectory directory,
    ICurrentUser currentUser)
{
    internal (string UserId, string OrganizationId) CurrentActor() => (
        currentUser.UserId
            ?? throw new UnauthorizedException("Authenticated user is required."),
        currentUser.OrganizationId
            ?? throw new UnauthorizedException("Active organization is required."));

    internal async Task<KnowledgeDocument> GetDocumentAsync(
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

    internal async Task<KnowledgeScopeAccess> AuthorizeDocumentAsync(
        KnowledgeDocument document,
        string organizationId,
        CancellationToken ct)
    {
        EnsureOrganization(document.OrganizationId, organizationId);
        var access = await directory.AuthorizeScopeAsync(document.ScopeType, document.ScopeId, ct);
        EnsureOrganization(access.OrganizationId, organizationId);
        return access;
    }

    internal static void EnsureOrganization(string actual, string expected)
    {
        if (!string.Equals(actual, expected, StringComparison.Ordinal))
        {
            throw new NotFoundException(
                "KNOWLEDGE_DOCUMENT_NOT_FOUND",
                "Knowledge document was not found.");
        }
    }
}
