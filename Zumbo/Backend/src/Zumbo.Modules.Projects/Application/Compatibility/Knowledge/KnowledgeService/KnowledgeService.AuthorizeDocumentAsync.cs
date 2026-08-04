using System.Text.RegularExpressions;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Projects;

public sealed partial class KnowledgeService{

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
}
