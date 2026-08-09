using Zumbo.Modules.Projects;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Projects.Application.Features.Knowledge;

internal sealed class GetKnowledgeLinkOptionsSlice(
    IKnowledgeDirectory directory,
    ICurrentUser currentUser)
{
    internal async Task<KnowledgeLinkOptionsResponse> HandleAsync(
        GetKnowledgeLinkOptionsQuery query,
        CancellationToken ct)
    {
        _ = currentUser.UserId
            ?? throw new UnauthorizedException("Authenticated user is required.");
        var organizationId = currentUser.OrganizationId
            ?? throw new UnauthorizedException("Active organization is required.");
        var access = await directory.AuthorizeScopeAsync(
            KnowledgeQueryInput.AllowedScope(query.ScopeType),
            KnowledgeQueryInput.Required(query.ScopeId, "Knowledge scope", 128),
            ct);
        KnowledgeReadAccess.EnsureOrganization(access.OrganizationId, organizationId);
        return await directory.ReadLinkOptionsAsync(
            organizationId,
            access.ProjectIds,
            KnowledgeQueryInput.Optional(query.Query, 100),
            ct);
    }
}
