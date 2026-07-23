using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.Modules.Identity;
using Zumbo.Modules.Boards;
using Zumbo.Modules.Organizations;
using Zumbo.Modules.Projects;
using Zumbo.Modules.Teams;
using Zumbo.Modules.WorkItems;
using Zumbo.SharedKernel;

public sealed class ProjectPermissionCheckerAdapter(
    IProjectResourcePolicy resourcePolicy) : IProjectPermissionChecker
{
    public Task<ProjectResourceAuthorization> EnsureCanAsync(
        string userId,
        string projectId,
        string permission,
        CancellationToken ct) =>
        resourcePolicy.AuthorizeAsync(projectId, permission, ct);
}

public sealed class ProjectResourcePolicyAdapter(
    IDocumentRepository<ProjectDocument> projects,
    IDocumentRepository<OrganizationDocument> organizations,
    ICurrentUser currentUser) : IProjectResourcePolicy
{
    public async Task<ProjectResourceAuthorization> AuthorizeAsync(
        string projectId,
        string permission,
        CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId;
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new UnauthorizedException("Authenticated user is required.");
        }

        var project = await projects.SelectAsync(x => x.Id == projectId && !x.Archived, cancellationToken)
            ?? throw new NotFoundException("PROJECT_NOT_FOUND", "Project was not found.");
        await EnsureOrganizationActiveAsync(organizations, project.OrganizationId, cancellationToken);
        if (PermissionCatalog.IsSystemAdministrator(currentUser.Roles))
        {
            return new ProjectResourceAuthorization(project.Id, project.OrganizationId, userId, null, true);
        }

        if (!string.Equals(currentUser.OrganizationId, project.OrganizationId, StringComparison.Ordinal))
        {
            throw new NotFoundException("PROJECT_NOT_FOUND", "Project was not found.");
        }

        var membership = project.Members.SingleOrDefault(x => x.UserId == userId);
        if (membership is null
            && ProjectVisibilityAccess.CanView(project.Visibility, [], userId)
            && ProjectVisibilityAccess.IsReadPermission(permission))
        {
            return new ProjectResourceAuthorization(project.Id, project.OrganizationId, userId, null, false);
        }

        if (membership is null)
        {
            throw new ForbiddenException("User is not a member of this project.");
        }

        if (!PermissionCatalog.HasProjectPermission(membership.Role, permission))
        {
            throw new ForbiddenException($"Project role '{membership.Role}' cannot perform '{permission}'.");
        }

        return new ProjectResourceAuthorization(
            project.Id,
            project.OrganizationId,
            userId,
            membership.Role,
            false);
    }

    private static async Task EnsureOrganizationActiveAsync(
        IDocumentRepository<OrganizationDocument> organizations,
        string organizationId,
        CancellationToken ct)
    {
        var organization = await organizations.SelectAsync(
            candidate => candidate.Id == organizationId || candidate.TenantKey == organizationId,
            ct)
            ?? throw new NotFoundException("ORGANIZATION_NOT_FOUND", "Project organization was not found.");
        if (!string.IsNullOrWhiteSpace(organization.Status)
            && organization.Status != OrganizationStatuses.Active)
        {
            throw new ConflictException("PROJECT_ORGANIZATION_INACTIVE", "Projects require an active organization.");
        }
    }
}

public sealed class ProjectMemberDirectoryAdapter(IUserRepository users) : IProjectMemberDirectory
{
    public async Task EnsureEligibleAsync(string userId, string organizationId, CancellationToken ct)
    {
        var user = await users.GetByIdAsync(userId, ct)
            ?? throw new NotFoundException("USER_NOT_FOUND", "Project member user was not found.");
        if (!user.IsActive)
        {
            throw new ConflictException("USER_INACTIVE", "Inactive users cannot be added to projects.");
        }

        if (!string.Equals(user.OrganizationId, organizationId, StringComparison.Ordinal))
        {
            throw new ConflictException("PROJECT_MEMBER_ORGANIZATION_MISMATCH", "Project members must belong to the project organization.");
        }
    }
}

public sealed class ProjectTeamDirectoryAdapter(IDocumentRepository<TeamDocument> teams) : IProjectTeamDirectory
{
    public async Task<ProjectTeamDirectoryEntry?> FindAsync(string teamId, CancellationToken ct)
    {
        var team = await teams.SelectAsync(x => x.Id == teamId, ct);
        return team is null
            ? null
            : new ProjectTeamDirectoryEntry(team.Id, team.OrganizationId, !team.Archived);
    }
}

public sealed class ProjectOrganizationDirectoryAdapter(
    IDocumentRepository<OrganizationDocument> organizations) : IProjectOrganizationDirectory
{
    public async Task EnsureActiveAsync(string organizationId, CancellationToken ct)
    {
        var organization = await organizations.SelectAsync(
            candidate => candidate.Id == organizationId || candidate.TenantKey == organizationId,
            ct)
            ?? throw new NotFoundException("ORGANIZATION_NOT_FOUND", "Project organization was not found.");
        if (!string.IsNullOrWhiteSpace(organization.Status)
            && organization.Status != OrganizationStatuses.Active)
        {
            throw new ConflictException("PROJECT_ORGANIZATION_INACTIVE", "Projects require an active organization.");
        }
    }
}

public sealed class ProjectTeamUsageCheckerAdapter(
    IDocumentRepository<WorkItemDocument> workItems) : IProjectTeamUsageChecker
{
    public async Task<bool> HasWorkItemsAsync(string projectId, string teamId, CancellationToken ct) =>
        await workItems.SelectAsync(x => x.ProjectId == projectId && x.TeamId == teamId, ct) is not null;
}

public sealed class IntakeRoutePolicyAdapter(
    IDocumentRepository<ProjectDocument> projects,
    IDocumentRepository<OrganizationDocument> organizations,
    IDocumentRepository<BoardDocument> boards) : IIntakeRoutePolicy
{
    public async Task<IntakeRouteAuthorization> ValidateAsync(
        string organizationId,
        string projectId,
        string boardId,
        CancellationToken ct)
    {
        var project = await projects.SelectAsync(
            x => x.Id == projectId
                && x.OrganizationId == organizationId
                && !x.Archived,
            ct)
            ?? throw new NotFoundException(
                "INTAKE_ROUTE_NOT_FOUND",
                "Intake route was not found.");
        var organization = await organizations.SelectAsync(
            x => x.Id == project.OrganizationId || x.TenantKey == project.OrganizationId,
            ct)
            ?? throw new NotFoundException(
                "INTAKE_ROUTE_NOT_FOUND",
                "Intake route was not found.");
        if (!string.IsNullOrWhiteSpace(organization.Status)
            && organization.Status != OrganizationStatuses.Active)
        {
            throw new ConflictException(
                "INTAKE_ORGANIZATION_INACTIVE",
                "Intake forms require an active organization.");
        }

        var board = await boards.SelectAsync(
            x => x.Id == boardId
                && x.ProjectId == project.Id
                && !x.Archived,
            ct)
            ?? throw new NotFoundException(
                "INTAKE_ROUTE_NOT_FOUND",
                "Intake route was not found.");
        return new IntakeRouteAuthorization(
            project.OrganizationId,
            project.Id,
            board.Id);
    }
}
