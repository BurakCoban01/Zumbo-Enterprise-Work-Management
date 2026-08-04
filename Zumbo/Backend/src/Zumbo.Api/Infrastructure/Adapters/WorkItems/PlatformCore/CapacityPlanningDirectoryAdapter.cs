using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.Modules.Audit;
using Zumbo.Modules.Identity;
using Zumbo.Modules.Projects;
using Zumbo.Modules.Teams;
using Zumbo.Modules.WorkItems;
using Zumbo.SharedKernel;

public sealed class CapacityPlanningDirectoryAdapter(
    IDocumentRepository<UserDocument> users,
    IDocumentRepository<TeamDocument> teams,
    IDocumentRepository<ProjectDocument> projects,
    IDocumentRepository<PortfolioDocument> portfolios,
    ICurrentUser currentUser,
    IProjectPermissionChecker projectPermissions) : ICapacityPlanningDirectory
{
    public async Task EnsureOrganizationUsersAndTeamsAsync(
        string organizationId,
        IReadOnlyCollection<CapacityMemberRequest> members,
        IReadOnlyCollection<string> viewerUserIds,
        CancellationToken ct)
    {
        var userIds = members.Select(item => item.UserId)
            .Concat(viewerUserIds)
            .Distinct(StringComparer.Ordinal);
        foreach (var userId in userIds)
        {
            if (!await users.ExistsByFilterAsync(
                    user => user.Id == userId
                        && user.OrganizationId == organizationId
                        && user.IsActive,
                    ct))
            {
                throw new ValidationException(
                    "Capacity-plan people must be active users in the plan organization.");
            }
        }

        foreach (var member in members.Where(item => item.TeamId is not null))
        {
            var team = await teams.SelectAsync(
                item => item.Id == member.TeamId
                    && item.OrganizationId == organizationId
                    && !item.Archived,
                ct);
            if (team is null
                || !team.Members.Any(item =>
                    item.UserId == member.UserId
                    && item.Status == "Active"))
            {
                throw new ValidationException(
                    "Capacity-plan team assignments must reference active team members.");
            }
        }
    }

    public async Task EnsureManageableScopeAsync(
        string organizationId,
        string actorUserId,
        string? portfolioId,
        IReadOnlyCollection<string> projectIds,
        CancellationToken ct)
    {
        if (portfolioId is not null)
        {
            var portfolio = await portfolios.SelectAsync(
                item => item.Id == portfolioId
                    && item.OrganizationId == organizationId
                    && !item.Archived,
                ct)
                ?? throw new ValidationException(
                    "Capacity-plan portfolio must be an active portfolio in the plan organization.");
            if (portfolio.OwnerUserId != actorUserId
                && !portfolio.ViewerUserIds.Contains(actorUserId, StringComparer.Ordinal))
            {
                throw new ValidationException("Capacity-plan portfolio is not readable.");
            }
            var portfolioProjects = portfolio.Initiatives
                .SelectMany(item => item.ProjectIds)
                .ToHashSet(StringComparer.Ordinal);
            if (projectIds.Any(projectId => !portfolioProjects.Contains(projectId)))
            {
                throw new ValidationException(
                    "Capacity-plan projects must belong to the linked portfolio.");
            }
        }

        foreach (var projectId in projectIds)
        {
            var project = await projects.SelectAsync(
                item => item.Id == projectId
                    && item.OrganizationId == organizationId
                    && !item.Archived,
                ct)
                ?? throw new ValidationException(
                    "Capacity-plan projects must be active projects in the plan organization.");
            if (PermissionCatalog.IsSystemAdministrator(currentUser.Roles))
                continue;
            var role = project.Members
                .SingleOrDefault(item => item.UserId == actorUserId)
                ?.Role;
            if (role is not (ProjectRoles.Owner or ProjectRoles.Admin))
            {
                throw new ValidationException("Capacity-plan project is not manageable.");
            }
        }
    }

    public async Task<IReadOnlyCollection<CapacityProjectAccess>> ReadProjectAccessAsync(
        string organizationId,
        string actorUserId,
        IReadOnlyCollection<string> projectIds,
        CancellationToken ct)
    {
        var result = new List<CapacityProjectAccess>();
        foreach (var projectId in projectIds)
        {
            var project = await projects.SelectAsync(
                item => item.Id == projectId
                    && item.OrganizationId == organizationId
                    && !item.Archived,
                ct);
            if (project is null)
            {
                result.Add(new CapacityProjectAccess(projectId, string.Empty, string.Empty, false));
                continue;
            }
            try
            {
                await projectPermissions.EnsureCanAsync(
                    actorUserId,
                    projectId,
                    PermissionCatalog.WorkItemView,
                    ct);
                result.Add(new CapacityProjectAccess(
                    project.Id,
                    project.Key,
                    project.Name,
                    true));
            }
            catch (NotFoundException)
            {
                result.Add(new CapacityProjectAccess(projectId, string.Empty, string.Empty, false));
            }
            catch (ForbiddenException)
            {
                result.Add(new CapacityProjectAccess(projectId, string.Empty, string.Empty, false));
            }
        }
        return result;
    }
}
