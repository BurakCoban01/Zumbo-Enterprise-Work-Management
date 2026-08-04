using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.Modules.Identity;
using Zumbo.Modules.Boards;
using Zumbo.Modules.Organizations;
using Zumbo.Modules.Projects;
using Zumbo.Modules.Teams;
using Zumbo.Modules.WorkItems;
using Zumbo.SharedKernel;

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
