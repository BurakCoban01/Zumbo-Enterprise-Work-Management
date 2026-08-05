using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

internal sealed class SetWorkItemTeamSlice(
    AssignmentMutationPipeline pipeline,
    IWorkItemTeamPolicy teamPolicy)
{
    internal async Task<WorkItemResponse> HandleAsync(
        SetWorkItemTeamCommand command,
        CancellationToken ct)
    {
        var workItem = await pipeline.LoadForTeamUpdateAsync(command.Id, ct);
        var teamId = NormalizeOptionalId(command.Request.TeamId);
        if (teamId is not null)
        {
            await teamPolicy.EnsureCanAssignAsync(
                workItem.ProjectId,
                teamId,
                workItem.AssigneeUserId,
                ct);
        }

        if (workItem.TeamId == teamId)
        {
            throw new ConflictException(
                "WORK_ITEM_TEAM_UNCHANGED",
                "Work item already has the requested team.");
        }

        var oldTeamId = workItem.TeamId;
        workItem.TeamId = teamId;
        return await pipeline.PersistTeamChangeAsync(
            workItem,
            oldTeamId,
            teamId,
            command.CorrelationId,
            ct);
    }

    private static string? NormalizeOptionalId(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
