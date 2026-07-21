using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.Modules.Identity;
using Zumbo.Modules.Projects;
using Zumbo.Modules.WorkItems;

public sealed class WorkItemCollaboratorDirectoryAdapter(
    IUserRepository users,
    IDocumentRepository<ProjectDocument> projects) : IWorkItemCollaboratorDirectory
{
    public async Task<bool> IsActiveProjectViewerAsync(
        string userId,
        string organizationId,
        string projectId,
        CancellationToken ct)
    {
        var user = await users.GetByIdAsync(userId, ct);
        if (user is null
            || !user.IsActive
            || !string.Equals(user.OrganizationId, organizationId, StringComparison.Ordinal))
        {
            return false;
        }

        var project = await projects.SelectAsync(
            item => item.Id == projectId
                && item.OrganizationId == organizationId
                && !item.Archived,
            ct);
        if (project is null)
        {
            return false;
        }

        var isMember = project.Members.Any(member => member.UserId == userId);
        return ProjectVisibilityAccess.CanView(project.Visibility, isMember ? [userId] : [], userId);
    }
}
