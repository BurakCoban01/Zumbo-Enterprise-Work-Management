using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.Modules.Identity;
using Zumbo.Modules.Boards;
using Zumbo.Modules.Organizations;
using Zumbo.Modules.Projects;
using Zumbo.Modules.Teams;
using Zumbo.Modules.WorkItems;
using Zumbo.SharedKernel;

public sealed class ProjectResourcePolicyAdapter(
    IDocumentRepository<ProjectDocument> projects,
    IDocumentRepository<OrganizationDocument> organizations,
    IdentityPermissionService identityPermissions,
    IdentityRoleCatalogService roleCatalog,
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
        if (await identityPermissions.HasPermissionAsync(PermissionCatalog.All, cancellationToken))
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

        if (!await roleCatalog.HasProjectPermissionAsync(membership.Role, permission, cancellationToken))
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
