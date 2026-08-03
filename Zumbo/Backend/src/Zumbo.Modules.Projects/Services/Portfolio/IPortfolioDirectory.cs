using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Projects;

public interface IPortfolioDirectory
{
    Task EnsureOrganizationUsersAsync(
        string organizationId,
        IReadOnlyCollection<string> userIds,
        CancellationToken ct);

    Task EnsureProjectsManageableAsync(
        string organizationId,
        IReadOnlyCollection<string> projectIds,
        CancellationToken ct);

    Task EnsureMilestoneLinksAsync(
        string organizationId,
        IReadOnlyCollection<PortfolioMilestoneLinkRequest> milestoneLinks,
        CancellationToken ct);

    Task<PortfolioProjectSourceResult> ReadProjectSourcesAsync(
        string organizationId,
        IReadOnlyCollection<string> projectIds,
        CancellationToken ct);
}
