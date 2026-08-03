using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Projects;

public sealed partial class GoalService{

    private async Task<GoalDocument> GetDocumentAsync(
        string goalId,
        bool includeArchived,
        CancellationToken ct)
    {
        var actor = CurrentActor();
        return await goals.SelectAsync(
            item => item.Id == goalId
                && item.OrganizationId == actor.OrganizationId
                && (includeArchived || !item.Archived),
            ct)
            ?? throw new NotFoundException("GOAL_NOT_FOUND", "Goal was not found.");
    }
}
