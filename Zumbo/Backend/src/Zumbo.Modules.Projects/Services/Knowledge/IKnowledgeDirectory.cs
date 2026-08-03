using System.Text.RegularExpressions;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Projects;

public interface IKnowledgeDirectory
{
    Task<KnowledgeScopeAccess> AuthorizeScopeAsync(
        string scopeType,
        string scopeId,
        CancellationToken ct);

    Task EnsureLinksAsync(
        string organizationId,
        IReadOnlyCollection<string> scopeProjectIds,
        IReadOnlyCollection<string> workItemIds,
        IReadOnlyCollection<string> userIds,
        CancellationToken ct);

    Task<KnowledgeLinkOptionsResponse> ReadLinkOptionsAsync(
        string organizationId,
        IReadOnlyCollection<string> scopeProjectIds,
        string? query,
        CancellationToken ct);
}
