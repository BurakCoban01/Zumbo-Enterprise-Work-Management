using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Projects;

public sealed partial class GoalService{

    public async Task<GoalRollupResponse> GetRollupAsync(
        string goalId,
        CancellationToken ct)
    {
        var actor = CurrentActor();
        var goal = await GetDocumentAsync(goalId, includeArchived: false, ct);
        EnsureVisible(goal, actor.UserId);
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
            Progress(goal),
            goal.Confidence,
            clock.UtcNow,
            sources.Initiatives,
            sources.Projects,
            sources.UnavailableSources);
    }
}
