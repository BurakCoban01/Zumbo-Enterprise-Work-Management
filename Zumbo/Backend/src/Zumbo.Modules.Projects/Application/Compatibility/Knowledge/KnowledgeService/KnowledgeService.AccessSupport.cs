using System.Text.RegularExpressions;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Projects;

public sealed partial class KnowledgeService
{
    private async Task<KnowledgeScopeAccess> AuthorizeDocumentAsync(
        KnowledgeDocument document,
        string organizationId,
        CancellationToken ct)
    {
        EnsureOrganization(document.OrganizationId, organizationId);
        var access = await directory.AuthorizeScopeAsync(
            document.ScopeType,
            document.ScopeId,
            ct);
        EnsureOrganization(access.OrganizationId, organizationId);
        return access;
    }

    private (string UserId, string OrganizationId) CurrentActor() => (
        currentUser.UserId
            ?? throw new UnauthorizedException("Authenticated user is required."),
        currentUser.OrganizationId
            ?? throw new UnauthorizedException("Active organization is required."));

    private static void EnsureCanEdit(
        KnowledgeDocument document,
        KnowledgeScopeAccess access,
        string userId)
    {
        if (document.OwnerUserId != userId && !access.CanManage)
            throw new ForbiddenException("Knowledge document edit access is required.");
    }

    private static void EnsureOrganization(string actual, string expected)
    {
        if (!string.Equals(actual, expected, StringComparison.Ordinal))
        {
            throw new NotFoundException(
                "KNOWLEDGE_DOCUMENT_NOT_FOUND",
                "Knowledge document was not found.");
        }
    }

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
