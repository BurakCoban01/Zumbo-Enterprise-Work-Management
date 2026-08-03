using System.Text.RegularExpressions;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Projects;

public sealed partial class KnowledgeService{

    public async Task<KnowledgeLinkOptionsResponse> GetLinkOptionsAsync(
        string scopeType,
        string scopeId,
        string? query,
        CancellationToken ct)
    {
        var actor = CurrentActor();
        var access = await directory.AuthorizeScopeAsync(
            AllowedScope(scopeType),
            Required(scopeId, "Knowledge scope", 128),
            ct);
        EnsureOrganization(access.OrganizationId, actor.OrganizationId);
        return await directory.ReadLinkOptionsAsync(
            actor.OrganizationId,
            access.ProjectIds,
            Optional(query, 100),
            ct);
    }
}
