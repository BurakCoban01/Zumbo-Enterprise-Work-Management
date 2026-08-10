using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.Modules.Projects;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Projects.Application.Features.Knowledge;

internal sealed class CreateKnowledgeDocumentSlice(
    IDocumentRepository<KnowledgeDocument> documents,
    IKnowledgeDirectory directory,
    IKnowledgeAuditWriter audit,
    ICurrentUser currentUser,
    IClock clock)
{
    internal async Task<KnowledgeDocumentResponse> HandleAsync(
        CreateKnowledgeDocumentCommand command,
        CancellationToken ct)
    {
        var userId = currentUser.UserId
            ?? throw new UnauthorizedException("Authenticated user is required.");
        var organizationId = currentUser.OrganizationId
            ?? throw new UnauthorizedException("Active organization is required.");
        var scopeType = KnowledgeQueryInput.AllowedScope(command.Request.ScopeType);
        var scopeId = KnowledgeQueryInput.Required(command.Request.ScopeId, "Knowledge scope", 128);
        var access = await directory.AuthorizeScopeAsync(scopeType, scopeId, ct);
        KnowledgeReadAccess.EnsureOrganization(access.OrganizationId, organizationId);
        if (!access.CanManage)
            throw new ForbiddenException("Project or initiative management access is required.");

        var version = KnowledgeVersionPolicy.Normalize(
            command.Request.Title,
            command.Request.ContentMarkdown,
            command.Request.Tags,
            command.Request.WorkItemIds,
            command.Request.UserIds,
            command.Request.ChangeSummary,
            userId,
            1,
            clock.UtcNow);
        await directory.EnsureLinksAsync(
            organizationId,
            access.ProjectIds,
            version.WorkItemIds,
            version.UserIds,
            ct);

        var document = new KnowledgeDocument
        {
            OrganizationId = organizationId,
            ScopeType = scopeType,
            ScopeId = scopeId,
            ScopeName = access.ScopeName,
            OwnerUserId = userId,
            CreatedAt = clock.UtcNow,
            UpdatedAt = clock.UtcNow
        };
        KnowledgeVersionPolicy.Apply(document, version);
        document.Versions.Add(version);
        document = await documents.CreateAsync(document, ct);
        await audit.WriteAsync(
            "KnowledgeDocumentCreated",
            document.Id,
            null,
            document.Title,
            command.CorrelationId,
            ct);
        return KnowledgeResponseMapper.ToDocument(document, access, userId);
    }
}
