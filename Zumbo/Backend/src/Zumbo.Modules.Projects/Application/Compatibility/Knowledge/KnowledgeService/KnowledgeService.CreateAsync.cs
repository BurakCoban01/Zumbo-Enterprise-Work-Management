using System.Text.RegularExpressions;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Projects;

public sealed partial class KnowledgeService{

    public async Task<KnowledgeDocumentResponse> CreateAsync(
        CreateKnowledgeDocumentRequest request,
        string correlationId,
        CancellationToken ct)
    {
        var actor = CurrentActor();
        var normalizedScopeType = AllowedScope(request.ScopeType);
        var scopeId = Required(request.ScopeId, "Knowledge scope", 128);
        var access = await directory.AuthorizeScopeAsync(normalizedScopeType, scopeId, ct);
        EnsureOrganization(access.OrganizationId, actor.OrganizationId);
        if (!access.CanManage)
            throw new ForbiddenException("Project or initiative management access is required.");

        var version = NormalizeVersion(
            request.Title,
            request.ContentMarkdown,
            request.Tags,
            request.WorkItemIds,
            request.UserIds,
            request.ChangeSummary,
            actor.UserId,
            1,
            clock.UtcNow);
        await directory.EnsureLinksAsync(
            actor.OrganizationId,
            access.ProjectIds,
            version.WorkItemIds,
            version.UserIds,
            ct);

        var document = new KnowledgeDocument
        {
            OrganizationId = actor.OrganizationId,
            ScopeType = normalizedScopeType,
            ScopeId = scopeId,
            ScopeName = access.ScopeName,
            OwnerUserId = actor.UserId,
            CreatedAt = clock.UtcNow,
            UpdatedAt = clock.UtcNow
        };
        Apply(document, version);
        document.Versions.Add(version);
        document = await documents.CreateAsync(document, ct);
        await audit.WriteAsync(
            "KnowledgeDocumentCreated",
            document.Id,
            null,
            document.Title,
            correlationId,
            ct);
        return ToResponse(document, access, actor.UserId);
    }
}
