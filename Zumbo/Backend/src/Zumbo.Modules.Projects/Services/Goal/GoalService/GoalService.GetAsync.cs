using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Projects;

public sealed partial class GoalService{

    public async Task<GoalResponse> GetAsync(
        string goalId,
        bool includeArchived,
        CancellationToken ct)
    {
        var actor = CurrentActor();
        var goal = await GetDocumentAsync(goalId, includeArchived, ct);
        EnsureVisible(goal, actor.UserId);
        return ToResponse(goal, actor.UserId);
    }
}
