using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.Modules.Identity;
using Zumbo.Modules.Boards;
using Zumbo.Modules.Organizations;
using Zumbo.Modules.Projects;
using Zumbo.Modules.Teams;
using Zumbo.Modules.WorkItems;
using Zumbo.SharedKernel;

public sealed class ProjectTeamUsageCheckerAdapter(
    IDocumentRepository<WorkItemDocument> workItems) : IProjectTeamUsageChecker
{
    public async Task<bool> HasWorkItemsAsync(string projectId, string teamId, CancellationToken ct) =>
        await workItems.SelectAsync(x => x.ProjectId == projectId && x.TeamId == teamId, ct) is not null;
}
