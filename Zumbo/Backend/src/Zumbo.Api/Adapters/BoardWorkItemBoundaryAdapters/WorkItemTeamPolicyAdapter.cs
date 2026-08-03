using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.Modules.Boards;
using Zumbo.Modules.Projects;
using Zumbo.Modules.Teams;
using Zumbo.Modules.WorkItems;
using Zumbo.Modules.Workflows;
using Zumbo.SharedKernel;

public sealed class WorkItemTeamPolicyAdapter(
    IDocumentRepository<ProjectDocument> projects,
    IDocumentRepository<TeamDocument> teams) : IWorkItemTeamPolicy
{
    public async Task EnsureCanAssignAsync(
        string projectId,
        string teamId,
        string? assigneeUserId,
        CancellationToken ct)
    {
        var project = await projects.SelectAsync(x => x.Id == projectId && !x.Archived, ct)
            ?? throw new NotFoundException("PROJECT_NOT_FOUND", "Project was not found.");
        if (!project.TeamIds.Contains(teamId))
        {
            throw new ConflictException("WORK_ITEM_TEAM_NOT_LINKED", "Team must be linked to the project.");
        }

        var team = await teams.SelectAsync(x => x.Id == teamId && !x.Archived, ct)
            ?? throw new NotFoundException("TEAM_NOT_FOUND", "Team was not found.");
        if (team.OrganizationId != project.OrganizationId)
        {
            throw new ConflictException("WORK_ITEM_TEAM_ORGANIZATION_MISMATCH", "Team must belong to the project organization.");
        }

        if (!string.IsNullOrWhiteSpace(assigneeUserId)
            && team.Members.All(x => x.UserId != assigneeUserId || x.Status != "Active"))
        {
            throw new ConflictException("WORK_ITEM_ASSIGNEE_NOT_IN_TEAM", "Assignee must be an active member of the work item team.");
        }
    }

    public async Task<IReadOnlyCollection<WorkItemTeamEntry>> ListProjectTeamsAsync(
        string projectId,
        CancellationToken ct)
    {
        var project = await projects.SelectAsync(x => x.Id == projectId && !x.Archived, ct)
            ?? throw new NotFoundException("PROJECT_NOT_FOUND", "Project was not found.");
        var teamIds = project.TeamIds.ToHashSet(StringComparer.Ordinal);
        var result = await teams.ListByFilterAsync(
            x => teamIds.Contains(x.Id) && !x.Archived,
            x => x.Name,
            pageSize: 100,
            cancellationToken: ct);
        return result.Select(x => new WorkItemTeamEntry(x.Id, x.Name)).ToList();
    }
}
