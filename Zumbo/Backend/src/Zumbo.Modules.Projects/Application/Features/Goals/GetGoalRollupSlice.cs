using Zumbo.SharedKernel;

namespace Zumbo.Modules.Projects.Application.Features.Goals;

internal sealed class GetGoalRollupSlice(
    GoalReadAccess access,
    IGoalDirectory directory,
    IClock clock)
{
    internal async Task<GoalRollupResponse> HandleAsync(
        GetGoalRollupQuery query,
        CancellationToken ct)
    {
        var actor = access.CurrentActor();
        var goal = await access.GetDocumentAsync(query.GoalId, includeArchived: false, ct);
        GoalReadAccess.EnsureVisible(goal, actor.UserId);
        var links = goal.InitiativeLinks
            .Select(item => new GoalInitiativeLinkRequest(item.PortfolioId, item.InitiativeId))
            .ToList();
        var sources = await directory.ReadSourcesAsync(
            goal.OrganizationId,
            links,
            goal.ProjectIds,
            ct);
        return new GoalRollupResponse(
            goal.Id,
            sources.UnavailableSources.Count == 0
                ? GoalSourceStatuses.Ready
                : GoalSourceStatuses.Partial,
            GoalResponseMapper.Progress(goal),
            goal.Confidence,
            clock.UtcNow,
            sources.Initiatives,
            sources.Projects,
            sources.UnavailableSources);
    }
}
