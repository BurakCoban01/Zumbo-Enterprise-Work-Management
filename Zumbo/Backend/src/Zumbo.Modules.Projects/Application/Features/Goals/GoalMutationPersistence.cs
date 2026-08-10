using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Projects.Application.Features.Goals;

internal sealed class GoalMutationPersistence(
    IDocumentRepository<GoalDocument> goals,
    ExpectedVersionState expectedVersion)
{
    internal async Task ReplaceAsync(GoalDocument goal, CancellationToken ct)
    {
        var result = await goals.ReplaceByVersionAsync(
            item => item.Id == goal.Id && item.OrganizationId == goal.OrganizationId,
            goal,
            expectedVersion.Consume(goal.Version),
            ct);
        if (!result.Found)
            throw new NotFoundException("GOAL_NOT_FOUND", "Goal was not found.");
        goal.Version = result.Version!.Value;
    }
}
