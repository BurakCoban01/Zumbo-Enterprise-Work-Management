using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Projects;

public interface IGoalDirectory
{
    Task EnsureOrganizationUsersAsync(
        string organizationId,
        IReadOnlyCollection<string> userIds,
        CancellationToken ct);

    Task EnsureSourcesReadableAsync(
        string organizationId,
        IReadOnlyCollection<GoalInitiativeLinkRequest> initiativeLinks,
        IReadOnlyCollection<string> projectIds,
        CancellationToken ct);

    Task<GoalSourceResult> ReadSourcesAsync(
        string organizationId,
        IReadOnlyCollection<GoalInitiativeLinkRequest> initiativeLinks,
        IReadOnlyCollection<string> projectIds,
        CancellationToken ct);
}
