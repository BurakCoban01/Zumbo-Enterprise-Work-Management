using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Projects;

internal sealed class ListProjectsSlice(
    IDocumentRepository<ProjectDocument> projects,
    IProjectOrganizationDirectory organizationDirectory,
    ICurrentUser currentUser)
{
    internal async Task<IReadOnlyList<ProjectResponse>> HandleAsync(
        ListProjectsQuery query,
        CancellationToken ct)
    {
        ListProjectsValidator.Validate(query);
        var normalizedOrganizationId = query.OrganizationId.Trim();
        EnsureOrganizationScope(normalizedOrganizationId);
        await organizationDirectory.EnsureActiveAsync(normalizedOrganizationId, ct);
        var userId = CurrentUserId();
        var result = await projects.ListByFilterAsync(
            project => project.OrganizationId == normalizedOrganizationId
                && project.Archived == query.Archived
                && (project.Visibility == ProjectVisibilities.Internal
                    || project.Members.Any(member => member.UserId == userId)),
            project => project.Key,
            pageSize: 100,
            cancellationToken: ct);
        return result.Select(ProjectResponseMapper.ToResponse).ToList();
    }

    private void EnsureOrganizationScope(string organizationId)
    {
        if (!IsSystemAdmin()
            && !string.Equals(currentUser.OrganizationId, organizationId.Trim(), StringComparison.Ordinal))
        {
            throw new ForbiddenException("User cannot access projects outside the current organization.");
        }
    }

    private string CurrentUserId() =>
        !string.IsNullOrWhiteSpace(currentUser.UserId)
            ? currentUser.UserId
            : throw new UnauthorizedException("Authenticated user is required.");

    private bool IsSystemAdmin() => PermissionCatalog.IsSystemAdministrator(currentUser.Roles);
}
