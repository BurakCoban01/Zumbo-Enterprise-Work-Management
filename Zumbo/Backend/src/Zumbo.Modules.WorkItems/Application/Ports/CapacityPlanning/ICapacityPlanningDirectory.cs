using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public interface ICapacityPlanningDirectory
{
    Task EnsureOrganizationUsersAndTeamsAsync(
        string organizationId,
        IReadOnlyCollection<CapacityMemberRequest> members,
        IReadOnlyCollection<string> viewerUserIds,
        CancellationToken ct);

    Task EnsureManageableScopeAsync(
        string organizationId,
        string actorUserId,
        string? portfolioId,
        IReadOnlyCollection<string> projectIds,
        CancellationToken ct);

    Task<IReadOnlyCollection<CapacityProjectAccess>> ReadProjectAccessAsync(
        string organizationId,
        string actorUserId,
        IReadOnlyCollection<string> projectIds,
        CancellationToken ct);
}
