using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.Modules.Boards;
using Zumbo.Modules.Projects;
using Zumbo.Modules.Teams;
using Zumbo.Modules.WorkItems;
using Zumbo.Modules.Workflows;
using Zumbo.SharedKernel;

public sealed class BoardProjectAccessCheckerAdapter(
    IProjectResourcePolicy resourcePolicy) : IBoardProjectAccessChecker
{
    public async Task EnsureCanAsync(string userId, string projectId, string permission, CancellationToken ct)
    {
        _ = await resourcePolicy.AuthorizeAsync(projectId, permission, ct);
    }
}
