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
