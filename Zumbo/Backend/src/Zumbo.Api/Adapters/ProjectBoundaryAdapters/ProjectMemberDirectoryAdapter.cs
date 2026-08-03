using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.Modules.Identity;
using Zumbo.Modules.Boards;
using Zumbo.Modules.Organizations;
using Zumbo.Modules.Projects;
using Zumbo.Modules.Teams;
using Zumbo.Modules.WorkItems;
using Zumbo.SharedKernel;

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
