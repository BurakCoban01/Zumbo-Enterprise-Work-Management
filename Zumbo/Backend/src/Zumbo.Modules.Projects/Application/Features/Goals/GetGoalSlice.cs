namespace Zumbo.Modules.Projects.Application.Features.Goals;

internal sealed class GetGoalSlice(GoalReadAccess access)
{
    internal async Task<GoalResponse> HandleAsync(GetGoalQuery query, CancellationToken ct)
    {
        var actor = access.CurrentActor();
        var goal = await access.GetDocumentAsync(query.GoalId, query.IncludeArchived, ct);
        GoalReadAccess.EnsureVisible(goal, actor.UserId);
        return GoalResponseMapper.ToResponse(goal, actor.UserId);
    }
}
